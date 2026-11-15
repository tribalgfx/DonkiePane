using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DonkiePane.Tasks;

namespace DonkiePane.Plugins.RestTasks;

/// <summary>Een taak zoals het protocol hem levert.</summary>
public sealed class RestTask
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("done")] public bool Done { get; set; }
    [JsonPropertyName("dueDate")] public string? DueDate { get; set; }

    /// <summary>Milliseconden sinds 1970 (UTC).</summary>
    [JsonPropertyName("createdAt")] public long CreatedAt { get; set; }
}

/// <summary>De lijst met taken plus de revisie waarmee je kunt zien of er iets veranderd is.</summary>
public sealed record RestTaskList(string Revision, IReadOnlyList<RestTask> Tasks);

/// <summary>De taak bestaat niet (meer) bij de bron.</summary>
public sealed class TaskGoneException() : TaskProviderException("Deze taak bestaat niet meer bij de bron.");

/// <summary>
/// De taal van het protocol "Tasks REST v1": <c>GET/POST/PATCH/DELETE {basis}/api/v1/tasks</c> met een
/// bearer-token, alleen via HTTPS. Een revisie (ook als ETag) laat een poll bijna niets kosten.
/// Weet niets van een specifiek dashboard.
/// </summary>
public sealed class RestTasksClient(HttpClient http, Uri baseUri, Func<string> token)
{
    public const int MaxTextLength = 256;

    /// <summary>
    /// De taken. <c>null</c> betekent "niet gewijzigd" (304): <paramref name="knownRevision"/> is nog actueel.
    /// </summary>
    public async Task<RestTaskList?> GetAsync(string? knownRevision, CancellationToken ct = default)
    {
        var (status, text) = await SendAsync(HttpMethod.Get, "api/v1/tasks", null, knownRevision, ct);
        if (status == HttpStatusCode.NotModified) return null;

        var doc = Parse<TasksResponse>(text) ?? throw new TaskProviderException("De bron gaf een onverwacht antwoord.");
        return new RestTaskList(doc.Revision ?? "", doc.Tasks ?? []);
    }

    /// <summary>
    /// Voegt een taak toe onder het gegeven <paramref name="id"/>. Met een eigen id is herhalen veilig:
    /// bestaat de taak al, dan verandert er niets en komt dezelfde taak terug.
    /// </summary>
    public async Task<RestTask> AddAsync(string id, string text, DateOnly? due, CancellationToken ct = default)
    {
        var body = new JsonObject { ["id"] = id, ["text"] = text };
        if (due is { } d) body["dueDate"] = DueString(d);
        var (_, response) = await SendAsync(HttpMethod.Post, "api/v1/tasks", body, null, ct);
        return Parse<TaskResponse>(response)?.Task ?? throw new TaskProviderException("De bron gaf een onverwacht antwoord.");
    }

    /// <summary>Wijzigt <c>text</c>, <c>done</c> en/of <c>dueDate</c> (<c>null</c> wist de deadline).</summary>
    public async Task<RestTask> PatchAsync(string id, JsonObject patch, CancellationToken ct = default)
    {
        var (_, response) = await SendAsync(HttpMethod.Patch, "api/v1/tasks/" + Uri.EscapeDataString(id), patch, null, ct);
        return Parse<TaskResponse>(response)?.Task ?? throw new TaskProviderException("De bron gaf een onverwacht antwoord.");
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default) =>
        await SendAsync(HttpMethod.Delete, "api/v1/tasks/" + Uri.EscapeDataString(id), null, null, ct);

    public static string DueString(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static T? Parse<T>(string json) where T : class =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json);

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(
        HttpMethod method, string relative, JsonNode? body, string? ifNoneMatchRevision, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUri, relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token());
        if (ifNoneMatchRevision is not null)
            request.Headers.TryAddWithoutValidation("If-None-Match", $"\"{ifNoneMatchRevision}\"");
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        string text;
        try
        {
            response = await http.SendAsync(request, ct);
            text = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            // De TLS-handdruk faalde: bij een pin is dat "andere afdruk", anders "niet vertrouwd".
            throw new TaskProviderException(
                "Het certificaat van de server klopt niet. Controleer de vingerafdruk in de instellingen van deze bron " +
                "(of laat hem leeg als de server een gewoon, vertrouwd certificaat heeft).", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new TaskProviderException("Geen verbinding met de bron.", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotModified)
                return (response.StatusCode, text);
            throw Translate(response, text);
        }
    }

    /// <summary>Foutcodes van het protocol naar een melding die de gebruiker kan gebruiken.</summary>
    private static TaskProviderException Translate(HttpResponseMessage response, string text)
    {
        var error = ErrorCode(text);
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                return new SourceUnauthorizedException(
                    "De bron accepteert het token niet (fout of ingetrokken). DonkiePane probeert het niet opnieuw, " +
                    "want de bron kan na een paar mislukte pogingen alles blokkeren. Maak een nieuw token en vul het in bij Instellingen.");
            case HttpStatusCode.TooManyRequests:
                return new SourceRateLimitedException(RetryAfter(response, text));
            case HttpStatusCode.Forbidden:
                return new TaskProviderException("De bron weigert de toegang (403). Mag dit profiel de taken nog gebruiken?");
            case HttpStatusCode.NotFound:
                return new TaskGoneException();
            case HttpStatusCode.RequestEntityTooLarge:
                return new TaskProviderException("De wijziging is te groot voor de bron.");
            case HttpStatusCode.BadRequest when error == "https_required":
                return new TaskProviderException("De bron eist HTTPS. Gebruik een adres dat met https:// begint.");
            case HttpStatusCode.BadRequest:
                return new TaskProviderException("De bron weigerde de wijziging (ongeldige gegevens, bijvoorbeeld een te lange tekst).");
            default:
                return new TaskProviderException($"De bron gaf een fout ({(int)response.StatusCode}).");
        }
    }

    private static string? ErrorCode(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>De wachttijd uit de header <c>Retry-After</c> (seconden), anders uit <c>retryAfter</c> in de body, anders een minuut.</summary>
    private static TimeSpan RetryAfter(HttpResponseMessage response, string json)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero) return delta;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("retryAfter", out var r) && r.TryGetInt32(out var seconds) && seconds > 0)
                return TimeSpan.FromSeconds(seconds);
        }
        catch (JsonException)
        {
        }
        return TimeSpan.FromMinutes(1);
    }

    private sealed class TasksResponse
    {
        [JsonPropertyName("revision")] public string? Revision { get; set; }
        [JsonPropertyName("tasks")] public List<RestTask>? Tasks { get; set; }
    }

    private sealed class TaskResponse
    {
        [JsonPropertyName("task")] public RestTask? Task { get; set; }
    }
}

using DonkiePane.Tasks;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DonkiePane.Plugins.Google;

/// <summary>
/// Dunne laag over de Google Tasks REST-API (<c>tasks.googleapis.com/tasks/v1</c>).
/// Weet niets van DonkiePane-taken; dat doet <see cref="GoogleTaskProvider"/>.
/// </summary>
/// <param name="http">Een HttpClient (met time-out).</param>
/// <param name="accessToken">Levert een toegangstoken; <c>true</c> = vers ophalen (na een afgewezen verzoek).</param>
public sealed class GoogleTasksClient(HttpClient http, Func<bool, CancellationToken, Task<string>> accessToken)
{
    private const string BaseUrl = "https://tasks.googleapis.com/tasks/v1/";

    public async Task<IReadOnlyList<GoogleTaskList>> ListListsAsync(CancellationToken ct = default)
    {
        var result = new List<GoogleTaskList>();
        string? pageToken = null;
        do
        {
            var url = "users/@me/lists?maxResults=1000" + PageToken(pageToken);
            var page = Parse<ListsPage>(await SendAsync(HttpMethod.Get, url, null, ct));
            result.AddRange((page?.Items ?? []).Select(l => new GoogleTaskList(l.Id, l.Title ?? "")));
            pageToken = page?.NextPageToken;
        } while (pageToken is not null);
        return result;
    }

    /// <summary>
    /// Alle taken van een lijst, ook voltooide. <c>showHidden</c> is nodig om taken te zien die in
    /// de Google-apps zelf zijn afgevinkt; zonder die vlag lijken ze te ontbreken.
    /// </summary>
    public async Task<IReadOnlyList<GoogleTaskDto>> ListTasksAsync(string listId, CancellationToken ct = default)
    {
        var result = new List<GoogleTaskDto>();
        string? pageToken = null;
        do
        {
            var url = $"lists/{Uri.EscapeDataString(listId)}/tasks?showCompleted=true&showHidden=true&maxResults=100" + PageToken(pageToken);
            var page = Parse<TasksPage>(await SendAsync(HttpMethod.Get, url, null, ct));
            result.AddRange((page?.Items ?? []).Where(t => t.Deleted != true));
            pageToken = page?.NextPageToken;
        } while (pageToken is not null);
        return result;
    }

    public async Task<GoogleTaskDto> InsertAsync(string listId, string title, DateOnly? due, CancellationToken ct = default)
    {
        var body = new JsonObject { ["title"] = title };
        if (due is { } d) body["due"] = DueString(d);
        var json = await SendAsync(HttpMethod.Post, $"lists/{Uri.EscapeDataString(listId)}/tasks", body, ct);
        return Parse<GoogleTaskDto>(json) ?? throw new TaskProviderException("Google Taken gaf een leeg antwoord.");
    }

    public Task PatchAsync(string listId, string taskId, JsonObject patch, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Patch, TaskUrl(listId, taskId), patch, ct);

    public Task DeleteAsync(string listId, string taskId, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, TaskUrl(listId, taskId), null, ct);

    /// <summary>
    /// Zet de taak direct onder <paramref name="previousId"/>; <c>null</c> = bovenaan. Een subtaak moet met
    /// <paramref name="parentId"/> onder zijn hoofdtaak blijven: laat je die weg, dan maakt Google er een gewone taak van.
    /// </summary>
    public Task MoveAsync(string listId, string taskId, string? previousId, string? parentId = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (previousId is not null) query.Add("previous=" + Uri.EscapeDataString(previousId));
        if (parentId is not null) query.Add("parent=" + Uri.EscapeDataString(parentId));
        var suffix = query.Count == 0 ? "" : "?" + string.Join("&", query);
        return SendAsync(HttpMethod.Post, TaskUrl(listId, taskId) + "/move" + suffix, null, ct);
    }

    /// <summary>Google bewaart bij <c>due</c> alleen de datum; het tijdstip wordt genegeerd.</summary>
    public static string DueString(DateOnly date) => date.ToString("yyyy-MM-dd") + "T00:00:00.000Z";

    private static string TaskUrl(string listId, string taskId) =>
        $"lists/{Uri.EscapeDataString(listId)}/tasks/{Uri.EscapeDataString(taskId)}";

    private static string PageToken(string? token) => token is null ? "" : "&pageToken=" + Uri.EscapeDataString(token);

    private static T? Parse<T>(string json) where T : class =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json);

    /// <summary>Verstuurt een verzoek; bij een 401 één keer opnieuw met een vers toegangstoken.</summary>
    private async Task<string> SendAsync(HttpMethod method, string relativeUrl, JsonNode? body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var token = await accessToken(attempt > 0, ct);
            using var request = new HttpRequestMessage(method, BaseUrl + relativeUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null)
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

            try
            {
                using var response = await http.SendAsync(request, ct);
                var text = await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
                if (response.IsSuccessStatusCode) return text;
                throw new TaskProviderException(ErrorMessage(response.StatusCode, text));
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                throw new TaskProviderException("Geen verbinding met Google Taken.", ex);
            }
        }
    }

    private static string ErrorMessage(HttpStatusCode status, string body)
    {
        string? detail = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            detail = doc.RootElement.GetProperty("error").GetProperty("message").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Geen leesbare foutmelding; de statuscode is dan genoeg.
        }

        return status switch
        {
            HttpStatusCode.Forbidden => "Google Taken weigert de toegang (403). Is de koppeling door de beheerder toegestaan? " + detail,
            HttpStatusCode.NotFound => "Deze taak of lijst bestaat niet meer bij Google Taken.",
            HttpStatusCode.TooManyRequests => "Google Taken krijgt te veel verzoeken; probeer het zo opnieuw.",
            _ => $"Google Taken gaf een fout ({(int)status}). {detail}".Trim(),
        };
    }

    private sealed class ListsPage
    {
        [JsonPropertyName("items")] public List<ListDto>? Items { get; set; }
        [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
    }

    private sealed class ListDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("title")] public string? Title { get; set; }
    }

    private sealed class TasksPage
    {
        [JsonPropertyName("items")] public List<GoogleTaskDto>? Items { get; set; }
        [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
    }
}



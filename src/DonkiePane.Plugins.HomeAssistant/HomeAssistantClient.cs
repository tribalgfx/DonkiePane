using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DonkiePane.Tasks;

namespace DonkiePane.Plugins.HomeAssistant;

/// <summary>Een to-do lijst in Home Assistant, bijvoorbeeld <c>todo.boodschappen</c>.</summary>
public sealed record HaList(string EntityId, string Title);

/// <summary>Een item van een to-do lijst zoals Home Assistant het teruggeeft.</summary>
public sealed class HaItem
{
    public string Uid { get; set; } = "";
    public string Summary { get; set; } = "";

    /// <summary><c>needs_action</c> of <c>completed</c>.</summary>
    public string Status { get; set; } = "needs_action";

    /// <summary>Een datum (<c>2026-12-24</c>) of datum met tijd; ontbreekt als er geen deadline is.</summary>
    public string? Due { get; set; }
}

/// <summary>
/// De REST-API van Home Assistant voor to-do lijsten. Weet alleen wat in de documentatie staat: lijsten staan als
/// <c>todo.*</c>-entiteiten in <c>/api/states</c>; items lees je met <c>todo.get_items</c> (met <c>?return_response</c>) en
/// wijzig je met <c>todo.add_item</c>, <c>todo.update_item</c> en <c>todo.remove_item</c> (zonder <c>?return_response</c>,
/// want die geven niets terug en weigeren de vlag met een 400).
/// </summary>
public sealed class HomeAssistantClient(HttpClient http, Uri baseUri, Func<string> token)
{
    public async Task<IReadOnlyList<HaList>> ListTodoListsAsync(CancellationToken ct = default)
    {
        var json = await SendAsync(HttpMethod.Get, "api/states", null, ct);
        using var doc = Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw Unexpected();

        var lists = new List<HaList>();
        foreach (var state in doc.RootElement.EnumerateArray())
        {
            var id = state.TryGetProperty("entity_id", out var e) ? e.GetString() : null;
            if (id is null || !id.StartsWith("todo.", StringComparison.Ordinal)) continue;

            string? name = null;
            if (state.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object
                && attributes.TryGetProperty("friendly_name", out var f))
                name = f.GetString();
            lists.Add(new HaList(id, string.IsNullOrWhiteSpace(name) ? id : name));
        }
        return lists.OrderBy(l => l.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Alle items, ook de afgeronde: zonder <c>status</c> geeft Home Assistant alleen de open items.</summary>
    public async Task<IReadOnlyList<HaItem>> GetItemsAsync(string entityId, CancellationToken ct = default)
    {
        var body = new JsonObject { ["entity_id"] = entityId, ["status"] = new JsonArray("needs_action", "completed") };
        var json = await SendAsync(HttpMethod.Post, "api/services/todo/get_items?return_response", body, ct);
        using var doc = Parse(json);

        if (!doc.RootElement.TryGetProperty("service_response", out var response)
            || !response.TryGetProperty(entityId, out var list)
            || !list.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            throw Unexpected();

        var result = new List<HaItem>();
        foreach (var item in items.EnumerateArray())
        {
            result.Add(new HaItem
            {
                Uid = item.TryGetProperty("uid", out var uid) ? uid.GetString() ?? "" : "",
                Summary = item.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "" : "",
                Status = item.TryGetProperty("status", out var status) ? status.GetString() ?? "needs_action" : "needs_action",
                Due = item.TryGetProperty("due", out var due) && due.ValueKind == JsonValueKind.String ? due.GetString() : null,
            });
        }
        return result;
    }

    /// <summary>Voegt een item toe. Home Assistant geeft het nieuwe uid niet terug; lees de lijst daarna opnieuw.</summary>
    public Task AddItemAsync(string entityId, string summary, DateOnly? due, CancellationToken ct = default)
    {
        var body = new JsonObject { ["entity_id"] = entityId, ["item"] = summary };
        if (due is { } d) body["due_date"] = DueString(d);
        return SendAsync(HttpMethod.Post, "api/services/todo/add_item", body, ct);
    }

    /// <summary>Wijzigt een item (op uid). <paramref name="changes"/> bevat <c>rename</c>, <c>status</c> en/of <c>due_date</c>.</summary>
    public Task UpdateItemAsync(string entityId, string uid, JsonObject changes, CancellationToken ct = default)
    {
        changes["entity_id"] = entityId;
        changes["item"] = uid;
        return SendAsync(HttpMethod.Post, "api/services/todo/update_item", changes, ct);
    }

    public Task RemoveItemAsync(string entityId, string uid, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, "api/services/todo/remove_item", new JsonObject { ["entity_id"] = entityId, ["item"] = uid }, ct);

    public static string DueString(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static JsonDocument Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new TaskProviderException("Home Assistant gaf een onverwacht antwoord.", ex);
        }
    }

    private static TaskProviderException Unexpected() => new("Home Assistant gaf een onverwacht antwoord. Is dit wel een to-do lijst?");

    private async Task<string> SendAsync(HttpMethod method, string relative, JsonNode? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(baseUri, relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token());
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode) return text;
            throw Translate(response.StatusCode, text);
        }
        catch (HttpRequestException ex) when (ex.InnerException is AuthenticationException)
        {
            throw new TaskProviderException(
                "Het certificaat van Home Assistant klopt niet. Controleer de vingerafdruk in de instellingen van deze bron " +
                "(of laat hem leeg als Home Assistant een gewoon, vertrouwd certificaat heeft).", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new TaskProviderException("Geen verbinding met Home Assistant.", ex);
        }
    }

    /// <summary>Statuscodes uit de documentatie van de REST-API naar een melding die de gebruiker kan gebruiken.</summary>
    private static TaskProviderException Translate(HttpStatusCode status, string body)
    {
        var detail = Detail(body);
        return status switch
        {
            HttpStatusCode.Unauthorized => new SourceUnauthorizedException(
                "Home Assistant accepteert het token niet (fout of ingetrokken). DonkiePane probeert het niet opnieuw, want na een paar " +
                "mislukte pogingen kan Home Assistant je adres blokkeren. Maak een nieuw token en vul het in bij Instellingen."),
            HttpStatusCode.NotFound => new TaskProviderException("Home Assistant kent deze lijst of dienst niet (404). Bestaat de to-do lijst nog?"),
            HttpStatusCode.BadRequest => new TaskProviderException("Home Assistant weigerde de aanvraag (400)." + detail),
            // Een herstart of afsluiten is tijdelijk: wijzigingen blijven wachten en gaan daarna vanzelf mee.
            HttpStatusCode.ServiceUnavailable => new SourceRateLimitedException(TimeSpan.FromSeconds(30)),
            HttpStatusCode.InternalServerError => new TaskProviderException("Home Assistant gaf een fout (500)." + detail),
            _ => new TaskProviderException($"Home Assistant gaf een fout ({(int)status})." + detail),
        };
    }

    /// <summary>De uitleg uit het antwoord van Home Assistant, kort en zonder opmaak.</summary>
    private static string Detail(string body)
    {
        var text = body.Trim();
        if (text.Length == 0) return "";
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("message", out var m) && m.GetString() is { } message)
                text = message;
        }
        catch (JsonException)
        {
            // Geen JSON: de tekst zelf is de uitleg.
        }
        return " " + (text.Length > 200 ? text[..200] + "…" : text);
    }
}

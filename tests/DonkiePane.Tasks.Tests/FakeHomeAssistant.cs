using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DonkiePane.Tasks.Tests;

internal sealed record HaRequest(HttpMethod Method, string Path, string Query, string? Bearer, JsonObject? Body);

/// <summary>
/// Een nagebootste Home Assistant die doet wat de documentatie zegt: lijsten als <c>todo.*</c>-entiteiten in /api/states,
/// <c>get_items</c> alleen met <c>?return_response</c> (en zonder <c>status</c> alleen de open items), en de schrijfacties
/// juist zonder die vlag (met de vlag een 400). Dat een deadline gewist wordt met <c>due_date: null</c> is een aanname,
/// want de documentatie zegt er niets over.
/// </summary>
internal sealed class FakeHomeAssistant : HttpMessageHandler
{
    public const string ValidToken = "ha-test-token";

    public Dictionary<string, (string Title, List<FakeHaItem> Items)> Lists { get; } = [];
    public List<HaRequest> Requests { get; } = [];
    public Queue<Exception> Failures { get; } = new();
    public HttpStatusCode? NextStatus { get; set; }
    public string NextStatusBody { get; set; } = "";
    public int FailedAttempts { get; private set; }
    private int _uid = 1;

    public FakeHaItem Add(string list, string summary, string status = "needs_action", string? due = null)
    {
        var item = new FakeHaItem { Uid = "uid-" + _uid++, Summary = summary, Status = status, Due = due };
        Lists[list].Items.Add(item);
        return item;
    }

    public void AddList(string entity, string title) => Lists[entity] = (title, []);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var text = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        JsonObject? body = text is null ? null : JsonNode.Parse(text) as JsonObject;
        var path = request.RequestUri!.AbsolutePath;
        var query = request.RequestUri.Query;
        Requests.Add(new HaRequest(request.Method, path, query, request.Headers.Authorization?.Parameter, body));

        if (Failures.Count > 0) throw Failures.Dequeue();
        if (request.Headers.Authorization?.Parameter != ValidToken)
        {
            FailedAttempts++;
            return Text(HttpStatusCode.Unauthorized, "401: Unauthorized");
        }
        if (NextStatus is { } status)
        {
            NextStatus = null;
            return Text(status, NextStatusBody);
        }

        if (path == "/api/states" && request.Method == HttpMethod.Get) return States();
        if (request.Method != HttpMethod.Post || !path.StartsWith("/api/services/todo/", StringComparison.Ordinal)) return Text(HttpStatusCode.NotFound, "404: Not Found");

        var service = path["/api/services/todo/".Length..];
        var wantsResponse = query.Contains("return_response", StringComparison.Ordinal);
        var returnsData = service == "get_items";
        // Volgens de documentatie: de vlag bij een dienst zonder antwoord, of weglaten bij een dienst met antwoord, geeft een 400.
        if (wantsResponse != returnsData) return Text(HttpStatusCode.BadRequest, "Service does not support responses or requires one");

        var entity = (string?)body?["entity_id"];
        if (entity is null || !Lists.TryGetValue(entity, out var list)) return Text(HttpStatusCode.NotFound, "Entity not found");

        return service switch
        {
            "get_items" => GetItems(entity, list.Items, body!),
            "add_item" => AddItem(list.Items, body!),
            "update_item" => UpdateItem(list.Items, body!),
            "remove_item" => RemoveItem(list.Items, body!),
            _ => Text(HttpStatusCode.NotFound, "404: Not Found"),
        };
    }

    private HttpResponseMessage States()
    {
        var states = new JsonArray { new JsonObject { ["entity_id"] = "light.keuken", ["state"] = "on", ["attributes"] = new JsonObject { ["friendly_name"] = "Keuken" } } };
        foreach (var (entity, (title, items)) in Lists)
            states.Add(new JsonObject
            {
                ["entity_id"] = entity,
                ["state"] = items.Count(i => i.Status == "needs_action").ToString(),
                ["attributes"] = new JsonObject { ["friendly_name"] = title },
            });
        return Json(HttpStatusCode.OK, states);
    }

    private static HttpResponseMessage GetItems(string entity, List<FakeHaItem> items, JsonObject body)
    {
        // Zonder status alleen de open items, zoals in de documentatie.
        var wanted = body["status"] switch
        {
            JsonArray array => array.Select(s => (string?)s).ToList(),
            JsonValue value => [(string?)value],
            _ => ["needs_action"],
        };
        var result = new JsonArray();
        foreach (var item in items.Where(i => wanted.Contains(i.Status)))
        {
            var node = new JsonObject { ["summary"] = item.Summary, ["uid"] = item.Uid, ["status"] = item.Status };
            if (item.Due is not null) node["due"] = item.Due;
            result.Add(node);
        }
        return Json(HttpStatusCode.OK, new JsonObject
        {
            ["changed_states"] = new JsonArray(),
            ["service_response"] = new JsonObject { [entity] = new JsonObject { ["items"] = result } },
        });
    }

    private HttpResponseMessage AddItem(List<FakeHaItem> items, JsonObject body)
    {
        var summary = (string?)body["item"];
        if (string.IsNullOrWhiteSpace(summary)) return Text(HttpStatusCode.BadRequest, "item is required");
        items.Add(new FakeHaItem { Uid = "uid-" + _uid++, Summary = summary, Due = (string?)body["due_date"] });
        return Json(HttpStatusCode.OK, new JsonArray());
    }

    private static HttpResponseMessage UpdateItem(List<FakeHaItem> items, JsonObject body)
    {
        var fields = new[] { "rename", "status", "due_date", "due_datetime", "description" };
        if (!fields.Any(body.ContainsKey)) return Text(HttpStatusCode.BadRequest, "Set at least one of the fields you want to change");

        var key = (string?)body["item"];
        var item = items.FirstOrDefault(i => i.Uid == key) ?? items.FirstOrDefault(i => i.Summary == key);
        if (item is null) return Text(HttpStatusCode.InternalServerError, "Unable to find to-do list item: " + key);

        if (body["rename"] is { } rename) item.Summary = (string)rename!;
        if (body["status"] is { } status) item.Status = (string)status!;
        if (body.ContainsKey("due_date")) item.Due = (string?)body["due_date"]; // null wist (aanname)
        return Json(HttpStatusCode.OK, new JsonArray());
    }

    private static HttpResponseMessage RemoveItem(List<FakeHaItem> items, JsonObject body)
    {
        var key = (string?)body["item"];
        var removed = items.RemoveAll(i => i.Uid == key);
        if (removed == 0) return Text(HttpStatusCode.InternalServerError, "Unable to find to-do list item: " + key);
        return Json(HttpStatusCode.OK, new JsonArray());
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    private static HttpResponseMessage Json(HttpStatusCode status, JsonNode node) =>
        new(status) { Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json") };
}

internal sealed class FakeHaItem
{
    public string Uid { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Status { get; set; } = "needs_action";
    public string? Due { get; set; }
}

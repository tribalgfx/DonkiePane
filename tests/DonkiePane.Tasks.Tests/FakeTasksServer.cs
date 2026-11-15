using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DonkiePane.Plugins.RestTasks;

namespace DonkiePane.Tasks.Tests;

/// <summary>Wat de nep-server ontving.</summary>
internal sealed record ServerRequest(HttpMethod Method, string Path, string? Bearer, string? IfNoneMatch, string? Body);

/// <summary>
/// Een nagebootste server die het protocol "Tasks REST v1" spreekt, zoals beschreven in de API-beschrijving:
/// revisie en ETag (304), een eigen id maakt een POST herhaalbaar, PATCH met <c>null</c> wist de deadline,
/// foutcodes 400/401/404/429, en na vijf mislukte pogingen blokkeert hij alles, ook met een geldig token.
/// </summary>
internal sealed class FakeTasksServer : HttpMessageHandler
{
    public const string ValidToken = "dd_test-token";
    private int _revision = 1;

    public List<RestTask> Tasks { get; } = [];
    public List<ServerRequest> Requests { get; } = [];

    /// <summary>Fouten die de volgende verzoeken krijgen (bijv. een netwerkfout), in volgorde.</summary>
    public Queue<Exception> Failures { get; } = new();

    public int FailedAttempts { get; set; }
    public bool LockedOut => FailedAttempts >= 5;

    /// <summary>Wat de nep-server als <c>Retry-After</c> meegeeft bij een blokkade, in seconden.</summary>
    public int RetryAfterSeconds { get; set; } = 900;

    public string Revision => $"rev{_revision}";

    /// <summary>Iets wijzigen "op een ander apparaat": de revisie verandert.</summary>
    public void Touch() => _revision++;

    public RestTask AddTask(string id, string text, bool done = false, string? due = null, long? createdAt = null)
    {
        var task = new RestTask { Id = id, Text = text, Done = done, DueDate = due, CreatedAt = createdAt ?? 1_768_000_000_000 + Tasks.Count };
        Tasks.Add(task);
        Touch();
        return task;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var ifNoneMatch = request.Headers.TryGetValues("If-None-Match", out var values) ? values.First() : null;
        var path = request.RequestUri!.AbsolutePath;
        Requests.Add(new ServerRequest(request.Method, path, request.Headers.Authorization?.Parameter, ifNoneMatch, body));

        if (Failures.Count > 0) throw Failures.Dequeue();

        // Wachtwoordcontrole: een verkeerd token telt als mislukte poging; na vijf volgt een blokkade voor iedereen.
        if (request.Headers.Authorization?.Parameter != ValidToken)
        {
            FailedAttempts++;
            return LockedOut ? Locked() : Error(HttpStatusCode.Unauthorized, "unauthenticated");
        }
        if (LockedOut) return Locked();
        FailedAttempts = 0; // een geldig token zet de teller terug

        if (path == "/api/v1/tasks")
        {
            if (request.Method == HttpMethod.Get) return List(ifNoneMatch);
            if (request.Method == HttpMethod.Post) return Add(body);
        }
        else if (path.StartsWith("/api/v1/tasks/", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(path["/api/v1/tasks/".Length..]);
            if (request.Method.Method == "PATCH") return Patch(id, body);
            if (request.Method == HttpMethod.Delete) return Delete(id);
        }
        return Error(HttpStatusCode.NotFound, "gone");
    }

    private HttpResponseMessage List(string? ifNoneMatch)
    {
        if (ifNoneMatch == $"\"{Revision}\"")
            return new HttpResponseMessage(HttpStatusCode.NotModified);

        var response = Json(HttpStatusCode.OK, new { revision = Revision, tasks = Tasks });
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{Revision}\"");
        return response;
    }

    private HttpResponseMessage Add(string? body)
    {
        if (!TryObject(body, out var json)) return Error(HttpStatusCode.BadRequest, "invalid_json");
        var text = (string?)json["text"];
        if (string.IsNullOrWhiteSpace(text) || text.Length > 256) return Error(HttpStatusCode.BadRequest, "invalid");

        var id = (string?)json["id"] ?? "srv-" + Guid.NewGuid().ToString("N")[..8];
        var existing = Tasks.FirstOrDefault(t => t.Id == id);
        if (existing is not null) return Json(HttpStatusCode.OK, new { task = existing, revision = Revision }); // herhaald verzoek

        var task = AddTask(id, text, (bool?)json["done"] ?? false, (string?)json["dueDate"], DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return Json(HttpStatusCode.Created, new { task, revision = Revision });
    }

    private HttpResponseMessage Patch(string id, string? body)
    {
        if (!TryObject(body, out var json)) return Error(HttpStatusCode.BadRequest, "invalid_json");
        if (json.Any(p => p.Key is not ("text" or "done" or "dueDate"))) return Error(HttpStatusCode.BadRequest, "invalid");
        var task = Tasks.FirstOrDefault(t => t.Id == id);
        if (task is null) return Error(HttpStatusCode.NotFound, "gone");

        if (json.ContainsKey("text"))
        {
            var text = (string?)json["text"];
            if (string.IsNullOrWhiteSpace(text) || text.Length > 256) return Error(HttpStatusCode.BadRequest, "invalid");
            task.Text = text;
        }
        if (json.ContainsKey("done")) task.Done = (bool)json["done"]!;
        if (json.ContainsKey("dueDate")) task.DueDate = (string?)json["dueDate"]; // null wist
        Touch();
        return Json(HttpStatusCode.OK, new { task, revision = Revision });
    }

    private HttpResponseMessage Delete(string id)
    {
        var task = Tasks.FirstOrDefault(t => t.Id == id);
        if (task is null) return Error(HttpStatusCode.NotFound, "gone");
        Tasks.Remove(task);
        Touch();
        return Json(HttpStatusCode.OK, new { ok = true, revision = Revision });
    }

    private HttpResponseMessage Locked()
    {
        var response = Json((HttpStatusCode)429, new { error = "too_many_attempts", retryAfter = RetryAfterSeconds });
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(RetryAfterSeconds));
        return response;
    }

    private static bool TryObject(string? body, out JsonObject json)
    {
        json = [];
        try
        {
            if (JsonNode.Parse(body ?? "") is not JsonObject parsed) return false;
            json = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string code) => Json(status, new { error = code });

    private static HttpResponseMessage Json(HttpStatusCode status, object value) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}


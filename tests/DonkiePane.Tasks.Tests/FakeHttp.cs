using System.Net;
using System.Text;
using System.Text.Json;

namespace DonkiePane.Tasks.Tests;

/// <summary>Wat er naar "Google" is gestuurd (de body is al uitgelezen, want het verzoek wordt daarna weggegooid).</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Bearer, string? Body)
{
    public string PathAndQuery => Uri.PathAndQuery;
}

/// <summary>Een nagebootste server: bepaalt per verzoek het antwoord en onthoudt alle verzoeken.</summary>
internal sealed class FakeHandler(Func<RecordedRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.Parameter, body);
        Requests.Add(recorded);
        return respond(recorded);
    }
}

internal static class Responses
{
    public static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Raw(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage NoContent() => new(HttpStatusCode.NoContent);
}

internal sealed class MemoryTokenStore(string? initial = null) : DonkiePane.Plugins.Google.ITokenStore
{
    private string? _token = initial;
    public string? Load() => _token;
    public void Save(string refreshToken) => _token = refreshToken;
    public void Delete() => _token = null;
}

internal sealed class TestTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}


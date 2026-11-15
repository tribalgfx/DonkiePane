using System.Net;
using System.Net.Sockets;
using System.Text;

namespace DonkiePane.Tasks.Google;

/// <summary>
/// Vangt de terugkeer van Google op na het inloggen in de browser: een klein
/// webservertje dat alleen op deze pc (127.0.0.1) luistert, op een vrije poort.
/// Eigen TcpListener in plaats van HttpListener, want die vraagt op Windows soms
/// beheerdersrechten om een adres te reserveren.
/// </summary>
public sealed class LoopbackReceiver : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public LoopbackReceiver()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
    }

    public int Port { get; }

    public string RedirectUri => $"http://127.0.0.1:{Port}/";

    /// <summary>
    /// Wacht tot de browser terugkomt met de inlogcode. Verzoeken met een andere
    /// <c>state</c> (of zonder code) worden genegeerd, zodat een willekeurige pagina
    /// in de browser geen inlogcode kan binnenschuiven.
    /// </summary>
    public async Task<string> WaitForCodeAsync(string expectedState, TimeSpan timeout, CancellationToken ct = default)
    {
        using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        overall.CancelAfter(timeout);
        try
        {
            while (true)
            {
                using var client = await _listener.AcceptTcpClientAsync(overall.Token);
                var target = await ReadRequestTargetAsync(client, overall.Token);
                var query = ParseQuery(target);
                query.TryGetValue("state", out var state);
                var stateOk = state == expectedState;

                if (stateOk && query.TryGetValue("error", out var error))
                {
                    await RespondAsync(client, 400, "Bad Request", Page("Inloggen is niet gelukt", "Je kunt dit venster sluiten en het in DonkiePane opnieuw proberen."), overall.Token);
                    throw new TaskProviderException($"Inloggen bij Google is geweigerd of mislukt ({error}).");
                }
                if (stateOk && query.TryGetValue("code", out var code) && code.Length > 0)
                {
                    await RespondAsync(client, 200, "OK", Page("Je bent ingelogd", "Je kunt dit venster sluiten en teruggaan naar DonkiePane."), overall.Token);
                    return code;
                }

                await RespondAsync(client, 404, "Not Found", "", overall.Token); // bijv. /favicon.ico
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TaskProviderException("Inloggen duurde te lang. Probeer het opnieuw.");
        }
    }

    /// <summary>De eerste regel van het verzoek, bijvoorbeeld <c>/?code=...&amp;state=...</c>; leeg bij een stilzittende verbinding.</summary>
    private static async Task<string> ReadRequestTargetAsync(TcpClient client, CancellationToken ct)
    {
        // Browsers openen soms een verbinding zonder iets te sturen; dan niet eeuwig wachten.
        using var perConnection = CancellationTokenSource.CreateLinkedTokenSource(ct);
        perConnection.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var reader = new StreamReader(client.GetStream(), Encoding.ASCII, false, 1024, leaveOpen: true);
            var line = await reader.ReadLineAsync(perConnection.Token);
            var parts = line?.Split(' ');
            return parts is { Length: >= 2 } && parts[0] == "GET" ? parts[1] : "";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "";
        }
    }

    internal static Dictionary<string, string> ParseQuery(string target)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var questionMark = target.IndexOf('?');
        if (questionMark < 0) return result;

        foreach (var pair in target[(questionMark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? "" : pair[(equals + 1)..];
            result[Uri.UnescapeDataString(key.Replace('+', ' '))] = Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        return result;
    }

    private static async Task RespondAsync(TcpClient client, int status, string reason, string body, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    private static string Page(string title, string text) =>
        $"<!doctype html><html lang=\"nl\"><meta charset=\"utf-8\"><title>DonkiePane</title>" +
        "<body style=\"font-family:Segoe UI,sans-serif;background:#201a1a;color:#f5f0e8;display:grid;place-items:center;height:100vh;margin:0\">" +
        $"<div style=\"text-align:center\"><h1>{title}</h1><p>{text}</p></div></body></html>";

    public void Dispose() => _listener.Stop();
}

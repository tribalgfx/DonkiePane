using System.Net;
using System.Text.Json;

namespace DonkiePane.Core.Updates;

/// <summary>Een gepubliceerde versie van DonkiePane.</summary>
/// <param name="Version">Het versienummer (uit de tag, bijvoorbeeld <c>v0.2.0</c>).</param>
/// <param name="Page">De releasepagina op GitHub.</param>
/// <param name="Notes">De release-opmerkingen (Markdown, zoals ze op GitHub staan).</param>
/// <param name="Installer">De installer, als die bij de release staat.</param>
public sealed record ReleaseInfo(Version Version, Uri Page, string Notes, ReleaseAsset? Installer);

/// <summary>Een installer bij een release.</summary>
/// <param name="Sha256">Het controlegetal dat GitHub zelf berekent (kleine letters), of <c>null</c> als het ontbreekt.</param>
public sealed record ReleaseAsset(string Name, Uri Download, long Size, string? Sha256);

/// <summary>
/// Vraagt bij GitHub de nieuwste (niet-voorlopige) release op. Dit kan zonder token, maar alleen bij een
/// publieke repository; bij een privérepository of als er nog geen release is, geeft GitHub 404.
/// </summary>
public sealed class ReleaseFeed(HttpClient http, string repository = ReleaseFeed.DefaultRepository)
{
    public const string DefaultRepository = "tribalgfx/DonkiePane";

    /// <summary>De naam van de installer bij een release: <c>DonkiePane-Setup-&lt;versie&gt;.exe</c>.</summary>
    public const string InstallerPrefix = "DonkiePane-Setup-";

    public Uri ReleasesPage => new($"https://github.com/{repository}/releases");

    /// <summary>De nieuwste release, of <c>null</c> als GitHub er geen kent.</summary>
    /// <exception cref="HttpRequestException">Geen verbinding of een fout van GitHub.</exception>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        // GitHub weigert verzoeken zonder User-Agent. Een algemene naam: er gaat niets over de gebruiker mee.
        request.Headers.UserAgent.ParseAdd("DonkiePane-UpdateCheck");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(ct), ReleasesPage);
    }

    /// <summary>Leest het antwoord van <c>/releases/latest</c>. Concepten, voorlopige versies en onleesbare tags geven <c>null</c>.</summary>
    public static ReleaseInfo? Parse(string json, Uri fallbackPage)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        // Bijv. null of [] (een proxy, een storing): een mislukte controle, geen crash.
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Het antwoord van GitHub is geen release.");
        if (Bool(root, "draft") || Bool(root, "prerelease")) return null;
        if (AppVersion.TryParse(Text(root, "tag_name")) is not { } version) return null;

        var page = HttpsUri(Text(root, "html_url")) ?? fallbackPage;
        var notes = Text(root, "body") ?? "";

        ReleaseAsset? installer = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = Text(asset, "name");
                if (name is null || !IsInstallerName(name)) continue;
                if (HttpsUri(Text(asset, "browser_download_url")) is not { } download) continue;
                var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n) ? n : 0;
                installer = new ReleaseAsset(name, download, size, Sha256From(Text(asset, "digest")));
                break;
            }
        }
        return new ReleaseInfo(version, page, notes, installer);
    }

    /// <summary>Alleen een gewone bestandsnaam van de vorm <c>DonkiePane-Setup-*.exe</c>, zonder mappen.</summary>
    internal static bool IsInstallerName(string name) =>
        name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && name.IndexOfAny(['/', '\\', ':']) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary><c>sha256:ABC…</c> → <c>abc…</c>; iets anders (een ander algoritme, geen 64 tekens) geeft <c>null</c>.</summary>
    internal static string? Sha256From(string? digest)
    {
        const string prefix = "sha256:";
        if (digest is null || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hex = digest[prefix.Length..].Trim().ToLowerInvariant();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    private static Uri? HttpsUri(string? text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? uri : null;

    // Alleen op objecten: TryGetProperty gooit bij een ander soort waarde (bijv. een getal in de lijst met bestanden).
    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

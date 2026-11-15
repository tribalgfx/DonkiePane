using System.IO;
using System.Text.Json;
using DonkiePane.Core;

namespace DonkiePane.App;

/// <summary>
/// Eerder stond Google Taken ingebouwd, met client-ID en secret in <c>google.json</c> en het inlogtoken in
/// <c>google.token</c>. Nu is het een bron zoals elke andere. Deze eenmalige overgang maakt daarvan een
/// verbinding, zodat bestaande Google-panes blijven werken zonder opnieuw in te loggen.
/// De oude bestanden blijven staan (als terugval); ze worden daarna niet meer gelezen.
/// </summary>
public static class LegacyGoogleMigration
{
    private const string GoogleTypeId = "google-tasks";

    /// <returns><c>true</c> als er iets is overgezet en de layout bewaard moet worden.</returns>
    public static bool Run(TaskSourceHost host, Layout layout, string? appDataFolder = null, Func<string?>? readLegacyToken = null)
    {
        if (host.Connections.Any(c => c.TypeId == GoogleTypeId)) return false;

        var folder = appDataFolder ?? Path.GetDirectoryName(Core.LayoutStore.DefaultPath)!;
        var (clientId, secret) = ReadLegacySettings(Path.Combine(folder, "google.json"));
        var legacyPanes = layout.Panes
            .Where(p => p is { Kind: PaneKind.Tasks, TaskSource: TaskSources.Google } && string.IsNullOrEmpty(p.SourceId))
            .ToList();
        if (string.IsNullOrWhiteSpace(clientId) && legacyPanes.Count == 0) return false;

        var connection = host.NewConnection(GoogleTypeId, "Google Taken");
        connection.Values["ClientId"] = clientId ?? "";
        var secrets = host.SecretsFor(connection.Id);
        if (!string.IsNullOrWhiteSpace(secret)) secrets.Set("ClientSecret", secret);

        var token = (readLegacyToken ?? (() => new DpapiTokenStore(Path.Combine(folder, "google.token")).Load()))();
        if (!string.IsNullOrWhiteSpace(token)) secrets.Set("refreshToken", token);

        host.Save(connection);
        foreach (var pane in legacyPanes)
        {
            pane.SourceId = connection.Id;
            pane.TaskSource = TaskSources.Local; // het oude veld wordt niet meer gebruikt
        }
        return true;
    }

    private static (string? ClientId, string? Secret) ReadLegacySettings(string path)
    {
        try
        {
            if (!File.Exists(path)) return (null, null);
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            string? Read(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            return (Read("ClientId"), Read("ClientSecret"));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return (null, null);
        }
    }
}

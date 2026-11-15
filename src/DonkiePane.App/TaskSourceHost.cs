using DonkiePane.Core;
using DonkiePane.Tasks;
using DonkiePane.Tasks.Plugins;

namespace DonkiePane.App;

/// <summary>
/// Beheert de ingestelde bronnen (verbindingen): welke er zijn, hun waarden en geheimen, en het
/// aanmaken van de bron zodra een pane hem nodig heeft. De app kent alleen het contract; wat een
/// bron werkelijk doet, zit in een plug-in.
/// </summary>
public sealed class TaskSourceHost : IDisposable
{
    private readonly SourcesStore _store;
    private readonly SourcesConfig _config;
    private readonly Func<string, ISecretStore> _secretsFor;
    private readonly Func<Uri, Task> _openBrowser;
    private readonly ITaskProvider _local;
    private readonly object _lock = new();
    private readonly Dictionary<string, ITaskSource> _sources = [];
    private readonly Dictionary<string, string> _failed = [];

    public TaskSourceHost(SourcesStore store, PluginLoadResult plugins, Func<string, ISecretStore> secretsFor,
        Func<Uri, Task> openBrowser, ITaskProvider local)
    {
        _store = store;
        _config = store.Load();
        Plugins = plugins;
        _secretsFor = secretsFor;
        _openBrowser = openBrowser;
        _local = local;
    }

    public PluginLoadResult Plugins { get; }

    public IReadOnlyList<SourceConnection> Connections => _config.Connections;

    /// <summary>Een verbinding is gewijzigd of verwijderd; panes die hem gebruiken moeten opnieuw beginnen.</summary>
    public event EventHandler<string>? SourceReset;

    public ITaskSourcePlugin? PluginFor(string typeId) =>
        Plugins.Plugins.FirstOrDefault(p => p.Plugin.TypeId == typeId)?.Plugin;

    public string TypeName(SourceConnection connection) =>
        PluginFor(connection.TypeId)?.DisplayName ?? $"{connection.TypeId} (plug-in niet geladen)";

    public ISecretStore SecretsFor(string connectionId) => _secretsFor(connectionId);

    /// <summary>Een nieuwe verbinding, nog niet opgeslagen: pas bij <see cref="Save"/> hoort hij erbij.</summary>
    public SourceConnection NewConnection(string typeId, string name) => new() { TypeId = typeId, Name = name };

    /// <summary>
    /// Slaat de verbinding op (voegt hem toe als hij nieuw is). Met <paramref name="resetSource"/> wordt de lopende bron
    /// opnieuw gemaakt, zodat nieuwe waarden gelden; laat dat weg als er niets is gewijzigd, want een nieuwe bron
    /// begint weer met vragen aan de server.
    /// </summary>
    public void Save(SourceConnection connection, bool resetSource = true)
    {
        lock (_lock)
        {
            if (!_config.Connections.Contains(connection)) _config.Connections.Add(connection);
            _store.Save(_config);
        }
        if (resetSource) Reset(connection.Id);
    }

    /// <summary>Verwijdert de verbinding en zijn geheimen. Taken bij de bron zelf blijven staan.</summary>
    public void Remove(string connectionId)
    {
        lock (_lock)
        {
            _config.Connections.RemoveAll(c => c.Id == connectionId);
            _store.Save(_config);
        }
        _secretsFor(connectionId).Clear();
        Reset(connectionId);
    }

    private void Reset(string connectionId)
    {
        lock (_lock)
        {
            if (_sources.Remove(connectionId, out var old)) old.Dispose();
            _failed.Remove(connectionId);
        }
        SourceReset?.Invoke(this, connectionId);
    }

    /// <summary>De bron van een verbinding, of <c>null</c> met de reden in <paramref name="error"/>.</summary>
    public ITaskSource? GetSource(string connectionId, out string? error)
    {
        lock (_lock)
        {
            if (_sources.TryGetValue(connectionId, out var existing))
            {
                error = null;
                return existing;
            }
            if (_failed.TryGetValue(connectionId, out error)) return null;

            var connection = _config.Connections.FirstOrDefault(c => c.Id == connectionId);
            if (connection is null)
            {
                error = "De bron van dit pane bestaat niet meer. Voeg hem opnieuw toe bij Instellingen, Takenbronnen.";
                return null;
            }

            var plugin = PluginFor(connection.TypeId);
            if (plugin is null)
            {
                error = $"Het soort bron '{connection.TypeId}' is niet beschikbaar: de plug-in is niet geladen. Zet hem terug in de plugins-map.";
                return Fail(connectionId, error);
            }

            try
            {
                var context = new SourceContext(new Dictionary<string, string>(connection.Values), _secretsFor(connectionId), _openBrowser);
                var source = plugin.Create(context);
                _sources[connectionId] = source;
                error = null;
                return source;
            }
#pragma warning disable CA1031 // een plug-in is code van buiten: wat hij ook gooit, de app moet blijven draaien
            catch (Exception ex)
#pragma warning restore CA1031
            {
                error = $"De bron '{connection.Name}' kon niet worden gestart: {ex.Message}";
                return Fail(connectionId, error);
            }
        }
    }

    private ITaskSource? Fail(string connectionId, string error)
    {
        _failed[connectionId] = error;
        return null;
    }

    /// <summary>De provider voor een takenpane: lokaal, een bron, of een provider die uitlegt waarom het niet gaat.</summary>
    public ITaskProvider ProviderFor(Pane pane)
    {
        if (string.IsNullOrEmpty(pane.SourceId)) return _local;
        var source = GetSource(pane.SourceId, out var error);
        return source?.Provider ?? new UnavailableTaskProvider(error ?? "Deze bron is niet beschikbaar.");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var source in _sources.Values) source.Dispose();
            _sources.Clear();
        }
    }
}

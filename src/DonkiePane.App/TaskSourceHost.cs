using System.IO;
using DonkiePane.Core;
using DonkiePane.Tasks;
using DonkiePane.Tasks.Offline;
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
    private readonly string _queueFolder;
    private readonly object _lock = new();
    private readonly Dictionary<string, ITaskSource> _sources = [];
    private readonly Dictionary<string, OfflineQueueProvider> _wrapped = [];
    private readonly Dictionary<string, string> _failed = [];

    /// <param name="queueFolder">Waar de wachtrijen met offline wijzigingen staan (één bestand per verbinding).</param>
    public TaskSourceHost(SourcesStore store, PluginLoadResult plugins, Func<string, ISecretStore> secretsFor,
        Func<Uri, Task> openBrowser, ITaskProvider local, string queueFolder)
    {
        _store = store;
        _config = store.Load();
        Plugins = plugins;
        _secretsFor = secretsFor;
        _openBrowser = openBrowser;
        _local = local;
        _queueFolder = queueFolder;
    }

    private string QueueFile(string connectionId) => IsSafeId(connectionId)
        ? Path.Combine(_queueFolder, connectionId + ".json")
        : throw new ArgumentException("Ongeldig id voor een verbinding.", nameof(connectionId));

    public PluginLoadResult Plugins { get; }

    public IReadOnlyList<SourceConnection> Connections => _config.Connections;

    /// <summary>Hoe vaak bronnen op veranderingen controleren. Geldt voor alle bronnen, ook die al draaien.</summary>
    public TimeSpan PollInterval { get; private set; } = SourceContext.DefaultPollInterval;

    public void SetPollInterval(TimeSpan interval)
    {
        lock (_lock)
        {
            PollInterval = interval;
            foreach (var source in _sources.Values)
                if (source.Provider is IRefreshableTaskProvider refreshable)
                    refreshable.PollInterval = interval;
        }
    }

    /// <summary>Een verbinding is gewijzigd of verwijderd; panes die hem gebruiken moeten opnieuw beginnen.</summary>
    public event EventHandler<string>? SourceReset;

    public ITaskSourcePlugin? PluginFor(string typeId) =>
        Plugins.Plugins.FirstOrDefault(p => p.Plugin.TypeId == typeId)?.Plugin;

    public string TypeName(SourceConnection connection) =>
        PluginFor(connection.TypeId)?.DisplayName ?? Loc.F("{0} (plug-in niet geladen)", connection.TypeId);

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
        Reset(connectionId);
        if (!IsSafeId(connectionId)) return; // nooit een bestand bij een id dat een pad kan zijn
        _secretsFor(connectionId).Clear();
        try
        {
            File.Delete(QueueFile(connectionId)); // wijzigingen die nog wachtten op een bron die je zelf verwijdert, vervallen
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bestand in gebruik: het wordt nooit meer gelezen, want de verbinding bestaat niet meer.
        }
    }

    /// <summary>
    /// Begint opnieuw met de bron van deze verbinding, zonder iets aan de instellingen te veranderen. Bijvoorbeeld na
    /// een geslaagde test: een bron die na een geweigerd token was gestopt, probeert het dan weer.
    /// </summary>
    public void Restart(string connectionId) => Reset(connectionId);

    /// <summary>
    /// Ids komen uit <c>sources.json</c> en <c>layout.json</c>, die met de hand aan te passen zijn. Ze worden ook
    /// bestandsnamen (wachtrij, geheimen), dus alleen veilige tekens: nooit een pad.
    /// </summary>
    internal static bool IsSafeId(string? id) =>
        id is { Length: > 0 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private void Reset(string connectionId)
    {
        lock (_lock)
        {
            if (_wrapped.Remove(connectionId, out var queue)) queue.Dispose();
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
            if (connection is not null && !IsSafeId(connection.Id))
                return Fail(connectionId, Loc.F("De bron '{0}' heeft een ongeldig id in sources.json. Verwijder hem en voeg hem opnieuw toe.", connection.Name));
            if (connection is null)
            {
                error = Loc.T("De bron van dit pane bestaat niet meer. Voeg hem opnieuw toe bij Instellingen, Takenbronnen.");
                return null;
            }

            var plugin = PluginFor(connection.TypeId);
            if (plugin is null)
            {
                error = Loc.F("Het soort bron '{0}' is niet beschikbaar: de plug-in is niet geladen. Zet hem terug in de plugins-map.", connection.TypeId);
                return Fail(connectionId, error);
            }

            try
            {
                var context = new SourceContext(new Dictionary<string, string>(connection.Values), _secretsFor(connectionId), _openBrowser, PollInterval);
                var source = plugin.Create(context);
                _sources[connectionId] = source;
                error = null;
                return source;
            }
#pragma warning disable CA1031 // een plug-in is code van buiten: wat hij ook gooit, de app moet blijven draaien
            catch (Exception ex)
#pragma warning restore CA1031
            {
                error = Loc.F("De bron '{0}' kon niet worden gestart: {1}", connection.Name, ex.Message);
                return Fail(connectionId, error);
            }
        }
    }

    private ITaskSource? Fail(string connectionId, string error)
    {
        _failed[connectionId] = error;
        return null;
    }

    /// <summary>
    /// De provider voor een takenpane: lokaal, een bron, of een provider die uitlegt waarom het niet gaat.
    /// Een gekoppelde bron zit in een wachtrij die wijzigingen bewaart zolang de bron niet bereikbaar is.
    /// </summary>
    public ITaskProvider ProviderFor(Pane pane)
    {
        if (string.IsNullOrEmpty(pane.SourceId)) return _local;
        var source = GetSource(pane.SourceId, out var error);
        if (source is null) return new UnavailableTaskProvider(error ?? Loc.T("Deze bron is niet beschikbaar."));

        lock (_lock)
        {
            if (!_wrapped.TryGetValue(pane.SourceId, out var queue))
            {
                queue = OfflineQueueProvider.Wrap(source.Provider, new PendingQueueStore(QueueFile(pane.SourceId)));
                _wrapped[pane.SourceId] = queue;
            }
            return queue;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var queue in _wrapped.Values) queue.Dispose();
            _wrapped.Clear();
            foreach (var source in _sources.Values) source.Dispose();
            _sources.Clear();
        }
    }
}

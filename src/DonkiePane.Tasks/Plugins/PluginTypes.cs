namespace DonkiePane.Tasks.Plugins;

public enum SettingKind
{
    Text,

    /// <summary>Een webadres; wordt gecontroleerd op <c>https://</c> door de bron zelf.</summary>
    Url,

    /// <summary>Een geheim (token, wachtwoord). Komt nooit in een instellingenbestand, alleen in de geheimenopslag.</summary>
    Secret,

    /// <summary>SHA-256-vingerafdruk van een certificaat (<c>AA:BB:…</c>) om vast te pinnen.</summary>
    Fingerprint,
}

/// <summary>
/// Eén invulveld van een bron. De app tekent het formulier zelf op basis van deze lijst, zodat een
/// plug-in geen eigen schermen of opslag nodig heeft en geheimen altijd op dezelfde veilige plek landen.
/// </summary>
public sealed record SettingField(string Key, string Label, SettingKind Kind, bool Required = true, string? Help = null);

/// <summary>Een knop in het instellingenvenster van een bron, bijvoorbeeld "Inloggen bij Google".</summary>
public sealed record PluginAction(string Id, string Label);

/// <summary>Een lijst waaruit je kiest bij het maken van een takenpane.</summary>
public sealed record TaskListInfo(string Id, string Title);

/// <summary>Uitkomst van "verbinding testen".</summary>
public sealed record SourceCheck(bool Ok, string Message);

/// <summary>Geheimenopslag van één verbinding. De app bewaart ze versleuteld (DPAPI); de plug-in ziet alleen deze twee methodes.</summary>
public interface ISecretStore
{
    string? Get(string key);

    /// <summary><c>null</c> of leeg wist het geheim.</summary>
    void Set(string key, string? value);

    /// <summary>Wist alles (bij het verwijderen van de verbinding).</summary>
    void Clear();
}

/// <summary>Wat een bron van de app krijgt bij het starten.</summary>
/// <param name="Values">De ingevulde, niet-geheime instellingen (sleutel is <see cref="SettingField.Key"/>).</param>
/// <param name="Secrets">Geheimen van deze verbinding.</param>
/// <param name="OpenBrowser">Opent een adres in de standaardbrowser (voor inloggen).</param>
public sealed class SourceContext(IReadOnlyDictionary<string, string> values, ISecretStore secrets, Func<Uri, Task> openBrowser)
{
    public IReadOnlyDictionary<string, string> Values { get; } = values;
    public ISecretStore Secrets { get; } = secrets;
    public Func<Uri, Task> OpenBrowser { get; } = openBrowser;

    public string Value(string key) => Values.TryGetValue(key, out var v) ? v.Trim() : "";
}

/// <summary>
/// Een soort bron (bijvoorbeeld "Google Taken" of "Taken via REST"). De app kent alleen deze interface;
/// plug-ins staan als aparte assembly in een <c>plugins</c>-map die de gebruiker zelf beheert.
/// </summary>
public interface ITaskSourcePlugin
{
    /// <summary>Stabiele naam die in de configuratie staat; verander die nooit.</summary>
    string TypeId { get; }

    string DisplayName { get; }

    /// <summary>Korte uitleg voor in het instellingenvenster (wat is dit, waar haal je de gegevens vandaan).</summary>
    string Description { get; }

    IReadOnlyList<SettingField> Fields { get; }

    IReadOnlyList<PluginAction> Actions { get; }

    /// <summary>Maakt één verbinding aan. Mag niet het netwerk op: dat gebeurt pas bij gebruik.</summary>
    ITaskSource Create(SourceContext context);
}

/// <summary>Eén verbinding met een bron, met de ingevulde gegevens.</summary>
public interface ITaskSource : IDisposable
{
    ITaskProvider Provider { get; }

    /// <summary>De lijsten waaruit je kiest bij een nieuw takenpane (bij een bron met één lijst: precies één).</summary>
    Task<IReadOnlyList<TaskListInfo>> GetListsAsync(CancellationToken ct = default);

    /// <summary>"Verbinding testen": controleert de gegevens zonder iets te wijzigen.</summary>
    Task<SourceCheck> TestAsync(CancellationToken ct = default);

    /// <summary>Voert een knop uit (<see cref="PluginAction.Id"/>) en geeft een melding terug.</summary>
    Task<string> RunActionAsync(string actionId, CancellationToken ct = default);
}

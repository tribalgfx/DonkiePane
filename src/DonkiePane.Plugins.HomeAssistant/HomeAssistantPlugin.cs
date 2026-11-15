using System.Net.Http;
using DonkiePane.Tasks;
using DonkiePane.Tasks.Plugins;
using DonkiePane.Tasks.Security;

namespace DonkiePane.Plugins.HomeAssistant;

/// <summary>
/// De to-do lijsten uit je eigen Home Assistant. Je maakt in Home Assistant een lang-levend toegangstoken
/// (Profiel, tabblad Beveiliging) en vult dat hier in; elk pane toont dan één lijst.
/// </summary>
public sealed class HomeAssistantPlugin : ITaskSourcePlugin
{
    public const string BaseUrlKey = "BaseUrl";
    public const string TokenKey = "Token";
    public const string AllowHttpKey = "AllowHttp";
    public const string FingerprintKey = "Fingerprint";

    public string TypeId => "home-assistant-todo";

    public string DisplayName => "Home Assistant (to-do lijsten)";

    public string Description =>
        "De to-do lijsten uit je Home Assistant. Maak in Home Assistant een lang-levend toegangstoken (je profiel, tabblad Beveiliging) en vul dat hier in. " +
        "Een pane toont één lijst. Home Assistant kent geen volgorde en geen \"belangrijk\"; een deadline kan alleen een datum zijn, het tijdstip valt weg.";

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new(BaseUrlKey, "Adres", SettingKind.Url, Required: true, Help: "Bijvoorbeeld https://ha.voorbeeld.nl of http://homeassistant.local:8123"),
        new(TokenKey, "Toegangstoken", SettingKind.Secret, Required: true, Help: "Het lang-levende token uit je Home Assistant-profiel."),
        new(AllowHttpKey, "Onversleutelde verbinding (http) toestaan", SettingKind.Bool, Required: false,
            Help: "Alleen voor Home Assistant op je eigen netwerk zonder https. Het token gaat dan onversleuteld over het netwerk; zet dit niet aan voor een adres buiten je huis."),
        new(FingerprintKey, "Vingerafdruk certificaat (SHA-256)", SettingKind.Fingerprint, Required: false,
            Help: "Alleen bij https met een zelfondertekend certificaat. Leeg = gewone certificaatcontrole van Windows."),
    ];

    public IReadOnlyList<PluginAction> Actions { get; } = [];

    public ITaskSource Create(SourceContext context) => new HomeAssistantSource(context);
}

/// <summary>Eén verbinding met een Home Assistant.</summary>
public sealed class HomeAssistantSource : ITaskSource
{
    private readonly string? _configError;
    private readonly HttpClient? _http;
    private readonly HomeAssistantClient? _client;
    private readonly HomeAssistantTaskProvider? _provider;

    /// <param name="handler">Alleen voor tests: een nagebootste Home Assistant in plaats van het netwerk.</param>
    public HomeAssistantSource(SourceContext context, HttpMessageHandler? handler = null)
    {
        try
        {
            var baseUri = ParseBaseUri(context.Value(HomeAssistantPlugin.BaseUrlKey), context.Flag(HomeAssistantPlugin.AllowHttpKey));
            var pin = CertPin.Normalize(context.Value(HomeAssistantPlugin.FingerprintKey));
            var token = context.Secrets.Get(HomeAssistantPlugin.TokenKey);
            if (string.IsNullOrWhiteSpace(token)) throw new FormatException("Vul het toegangstoken in.");

            _http = new HttpClient(handler ?? PinnedHttp.CreateHandler(pin)) { Timeout = TimeSpan.FromSeconds(30) };
            _client = new HomeAssistantClient(_http, baseUri, () => context.Secrets.Get(HomeAssistantPlugin.TokenKey)!.Trim());
            _provider = new HomeAssistantTaskProvider(_client) { PollInterval = context.PollInterval };
        }
        catch (FormatException ex)
        {
            _configError = ex.Message;
        }
    }

    public ITaskProvider Provider => (ITaskProvider?)_provider ?? new UnavailableTaskProvider(_configError!);

    public async Task<IReadOnlyList<TaskListInfo>> GetListsAsync(CancellationToken ct = default)
    {
        var lists = await Client().ListTodoListsAsync(ct);
        return lists.Select(l => new TaskListInfo(l.EntityId, l.Title)).ToList();
    }

    public async Task<SourceCheck> TestAsync(CancellationToken ct = default)
    {
        try
        {
            var lists = await Client().ListTodoListsAsync(ct);
            return lists.Count == 0
                ? new SourceCheck(false, "Verbonden, maar Home Assistant heeft geen to-do lijsten. Voeg er een toe via de integratie 'To-do list' of 'Shopping list'.")
                : new SourceCheck(true, $"Verbonden. {lists.Count} {(lists.Count == 1 ? "lijst" : "lijsten")} gevonden.");
        }
        catch (TaskProviderException ex)
        {
            return new SourceCheck(false, ex.Message);
        }
    }

    public Task<string> RunActionAsync(string actionId, CancellationToken ct = default) =>
        throw new TaskProviderException($"Onbekende actie: {actionId}");

    public void Dispose()
    {
        _provider?.Dispose();
        _http?.Dispose();
    }

    private HomeAssistantClient Client() =>
        _client ?? throw new TaskProviderException(_configError ?? "Deze bron is niet ingesteld.");

    /// <summary>
    /// Een https-adres, of een http-adres als dat uitdrukkelijk is toegestaan. Zonder die keuze geldt: een token gaat
    /// nooit onversleuteld over het netwerk.
    /// </summary>
    public static Uri ParseBaseUri(string text, bool allowHttp)
    {
        if (text.Length == 0) throw new FormatException("Vul het adres van Home Assistant in.");
        if (!Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && !(allowHttp && uri.Scheme == Uri.UriSchemeHttp)))
        {
            throw new FormatException(allowHttp
                ? "Het adres moet met https:// of http:// beginnen."
                : "Het adres moet met https:// beginnen. Draait je Home Assistant zonder https op je eigen netwerk, zet dan 'Onversleutelde verbinding (http) toestaan' aan.");
        }
        return uri;
    }
}

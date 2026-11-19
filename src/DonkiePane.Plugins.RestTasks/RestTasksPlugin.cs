using System.Net.Http;
using DonkiePane.Tasks;
using DonkiePane.Tasks.Plugins;
using DonkiePane.Tasks.Security;

namespace DonkiePane.Plugins.RestTasks;

/// <summary>
/// Taken van elk dashboard of elke dienst die het protocol "Tasks REST v1" spreekt. Er staat hier geen
/// enkel specifiek dashboard in: jij vult het adres, het token en (bij een zelfondertekend certificaat)
/// de vingerafdruk in.
/// </summary>
public sealed class RestTasksPlugin : ITaskSourcePlugin
{
    public const string BaseUrlKey = "BaseUrl";
    public const string TokenKey = "Token";
    public const string FingerprintKey = "Fingerprint";

    public string TypeId => "tasks-rest-v1";

    public string DisplayName => "Taken via REST (Tasks REST v1)";

    public string Description =>
        "Voor een dashboard of dienst die het protocol 'Tasks REST v1' spreekt: GET, POST, PATCH en DELETE op {adres}/api/v1/tasks, " +
        "met een bearer-token, alleen via HTTPS. Je maakt het token in dat dashboard zelf. Heeft de server een zelfondertekend " +
        "certificaat, vul dan de SHA-256-vingerafdruk in; DonkiePane accepteert dan precies dat certificaat en niets anders.";

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new(BaseUrlKey, "Adres", SettingKind.Url, Required: true, Help: "Bijvoorbeeld https://dashboard.thuis:4173 (zonder /api/…)"),
        new(TokenKey, "Token", SettingKind.Secret, Required: true, Help: "Het persoonlijke API-token uit je dashboard."),
        new(FingerprintKey, "Vingerafdruk certificaat (SHA-256)", SettingKind.Fingerprint, Required: false,
            Help: "AA:BB:CC:… Alleen nodig bij een zelfondertekend certificaat. Leeg = gewone certificaatcontrole van Windows. Krijgt de server een nieuw certificaat, vul dan de nieuwe afdruk in."),
    ];

    public IReadOnlyList<PluginAction> Actions { get; } = [];

    public ITaskSource Create(SourceContext context) => new RestTaskSource(context);
}

/// <summary>Eén verbinding met een Tasks REST v1-bron.</summary>
public sealed class RestTaskSource : ITaskSource
{
    private readonly string? _configError;
    private readonly HttpClient? _http;
    private readonly RestTasksClient? _client;
    private readonly RestTaskProvider? _provider;

    /// <param name="handler">Alleen voor tests: een nagebootste server in plaats van het netwerk.</param>
    public RestTaskSource(SourceContext context, HttpMessageHandler? handler = null)
    {
        try
        {
            var baseUri = ParseBaseUri(context.Value(RestTasksPlugin.BaseUrlKey));
            var pin = CertPin.Normalize(context.Value(RestTasksPlugin.FingerprintKey));
            var token = context.Secrets.Get(RestTasksPlugin.TokenKey);
            if (string.IsNullOrWhiteSpace(token)) throw new FormatException("Vul het token in.");

            _http = new HttpClient(handler ?? PinnedHttp.CreateHandler(pin))
            {
                Timeout = TimeSpan.FromSeconds(30),
                MaxResponseContentBufferSize = PinnedHttp.MaxResponseBytes,
            };
            _client = new RestTasksClient(_http, baseUri, () => context.Secrets.Get(RestTasksPlugin.TokenKey)!.Trim());
            _provider = new RestTaskProvider(_client) { PollInterval = context.PollInterval };
        }
        catch (FormatException ex)
        {
            _configError = ex.Message;
        }
    }

    public ITaskProvider Provider =>
        (ITaskProvider?)_provider ?? new UnavailableTaskProvider(_configError!);

    public async Task<IReadOnlyList<TaskListInfo>> GetListsAsync(CancellationToken ct = default)
    {
        // Eerst controleren dat het adres en het token kloppen, zodat er geen pane van komt dat niets laat zien.
        await Client().GetAsync(null, ct);
        return [new TaskListInfo(RestTaskProvider.ListId, "Taken")];
    }

    public async Task<SourceCheck> TestAsync(CancellationToken ct = default)
    {
        try
        {
            var list = await Client().GetAsync(null, ct);
            var count = list?.Tasks.Count ?? 0;
            return new SourceCheck(true, $"Verbonden. {count} {(count == 1 ? "taak" : "taken")} gevonden.");
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

    private RestTasksClient Client() =>
        _client ?? throw new TaskProviderException(_configError ?? "Deze bron is niet ingesteld.");

    /// <summary>Alleen https; een adres zonder dat of met iets anders dan een adres wordt geweigerd.</summary>
    public static Uri ParseBaseUri(string text)
    {
        if (text.Length == 0) throw new FormatException("Vul het adres van de bron in.");
        if (!Uri.TryCreate(text.EndsWith('/') ? text : text + "/", UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new FormatException("Het adres moet met https:// beginnen. Een token mag nooit onversleuteld over het netwerk.");
        return uri;
    }
}


using DonkiePane.Tasks;
using DonkiePane.Tasks.Plugins;

namespace DonkiePane.Plugins.Google;

/// <summary>
/// Google Taken als bron. Je maakt zelf een Google Cloud-project en een OAuth-client (type "Desktop app");
/// er wordt bewust geen client-ID meegeleverd. Zie de handleiding in de documentatie.
/// </summary>
public sealed class GoogleSourcePlugin : ITaskSourcePlugin
{
    public const string ClientIdKey = "ClientId";
    public const string ClientSecretKey = "ClientSecret";
    public const string RefreshTokenKey = "refreshToken";

    public string TypeId => "google-tasks";

    public string DisplayName => "Google Taken";

    public string Description =>
        "Je taken uit Google Taken. Je maakt zelf een Google Cloud-project en een OAuth-client (type Desktop app) en vult hier het client-ID en het secret in. " +
        "Je wachtwoord vul je alleen bij Google zelf in, in je browser. Werk je met een Workspace-account, dan kan de beheerder de koppeling blokkeren.";

    public IReadOnlyList<SettingField> Fields { get; } =
    [
        new(ClientIdKey, "Client-ID", SettingKind.Text, Required: true, Help: "Eindigt op .apps.googleusercontent.com"),
        new(ClientSecretKey, "Client-secret", SettingKind.Secret, Required: false, Help: "Voor een desktop-app is dit soms niet nodig."),
    ];

    public IReadOnlyList<PluginAction> Actions { get; } =
    [
        new("signin", "Inloggen bij Google"),
        new("signout", "Uitloggen"),
    ];

    public ITaskSource Create(SourceContext context) => new GoogleTaskSource(context);
}

/// <summary>Eén Google-verbinding.</summary>
internal sealed class GoogleTaskSource : ITaskSource
{
    private readonly SourceContext _context;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = DonkiePane.Tasks.Security.PinnedHttp.MaxResponseBytes };
    private readonly GoogleAuth _auth;
    private readonly GoogleTasksClient _client;
    private readonly GoogleTaskProvider _provider;

    public GoogleTaskSource(SourceContext context)
    {
        _context = context;
        _auth = new GoogleAuth(_http, new SecretTokenStore(context.Secrets), ReadConfig);
        _client = new GoogleTasksClient(_http, (force, ct) => _auth.GetAccessTokenAsync(force, ct));
        _provider = new GoogleTaskProvider(_client) { PollInterval = context.PollInterval };
    }

    public ITaskProvider Provider => _provider;

    private GoogleClientConfig? ReadConfig()
    {
        var id = _context.Value(GoogleSourcePlugin.ClientIdKey);
        if (id.Length == 0) return null;
        var secret = _context.Secrets.Get(GoogleSourcePlugin.ClientSecretKey);
        return new GoogleClientConfig(id, string.IsNullOrWhiteSpace(secret) ? null : secret.Trim());
    }

    public async Task<IReadOnlyList<TaskListInfo>> GetListsAsync(CancellationToken ct = default)
    {
        if (!_auth.IsSignedIn) throw new NotSignedInException();
        var lists = await _client.ListListsAsync(ct);
        return lists.Select(l => new TaskListInfo(l.Id, l.Title)).ToList();
    }

    public async Task<SourceCheck> TestAsync(CancellationToken ct = default)
    {
        if (ReadConfig() is null) return new SourceCheck(false, "Vul eerst het client-ID in.");
        if (!_auth.IsSignedIn) return new SourceCheck(false, "Niet ingelogd bij Google. Kies 'Inloggen bij Google'.");
        try
        {
            var lists = await _client.ListListsAsync(ct);
            return new SourceCheck(true, $"Verbonden met Google Taken ({lists.Count} lijst{(lists.Count == 1 ? "" : "en")}).");
        }
        catch (TaskProviderException ex)
        {
            return new SourceCheck(false, ex.Message);
        }
    }

    public async Task<string> RunActionAsync(string actionId, CancellationToken ct = default)
    {
        switch (actionId)
        {
            case "signin":
                await _auth.SignInAsync(_context.OpenBrowser, ct);
                return "Ingelogd bij Google.";
            case "signout":
                await _auth.SignOutAsync(ct);
                return "Uitgelogd.";
            default:
                throw new TaskProviderException($"Onbekende actie: {actionId}");
        }
    }

    public void Dispose()
    {
        _provider.Dispose();
        _http.Dispose();
    }

    /// <summary>Het inlogtoken van Google bewaren in de versleutelde geheimenopslag van deze verbinding.</summary>
    private sealed class SecretTokenStore(ISecretStore secrets) : ITokenStore
    {
        public string? Load() => secrets.Get(GoogleSourcePlugin.RefreshTokenKey);
        public void Save(string refreshToken) => secrets.Set(GoogleSourcePlugin.RefreshTokenKey, refreshToken);
        public void Delete() => secrets.Set(GoogleSourcePlugin.RefreshTokenKey, null);
    }
}

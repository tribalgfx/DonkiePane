using System.Diagnostics;
using System.Net.Http;
using DonkiePane.Tasks;
using DonkiePane.Tasks.Google;

namespace DonkiePane.App;

/// <summary>
/// Alles rond het Google-account op deze pc: instellingen (client-ID), inloggen,
/// uitloggen en de bron van taken. Er gaat pas iets naar Google zodra er een
/// Google-pane is of je inlogt.
/// </summary>
public sealed class GoogleAccount : IDisposable
{
    private readonly GoogleSettingsStore _settingsStore = new(GoogleSettingsStore.DefaultPath);
    private readonly GoogleSettings _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public GoogleAccount()
    {
        _settings = _settingsStore.Load();
        Auth = new GoogleAuth(_http, new DpapiTokenStore(), () => _settings.ToConfig());
        Client = new GoogleTasksClient(_http, (force, ct) => Auth.GetAccessTokenAsync(force, ct));
        Provider = new GoogleTaskProvider(Client);
    }

    public GoogleAuth Auth { get; }
    public GoogleTasksClient Client { get; }
    public GoogleTaskProvider Provider { get; }

    public bool IsSignedIn => Auth.IsSignedIn;
    public string ClientId => _settings.ClientId;
    public bool HasClientSecret => !string.IsNullOrWhiteSpace(_settings.ClientSecret);

    /// <summary>Bewaart het client-ID en (als <paramref name="secret"/> niet <c>null</c> is) het secret. Een leeg secret wist het.</summary>
    public void SaveSettings(string clientId, string? secret)
    {
        _settings.ClientId = clientId.Trim();
        if (secret is not null) _settings.ClientSecret = string.IsNullOrWhiteSpace(secret) ? null : secret.Trim();
        _settingsStore.Save(_settings);
    }

    public Task SignInAsync(CancellationToken ct = default) => Auth.SignInAsync(OpenBrowser, ct);

    public Task SignOutAsync(CancellationToken ct = default) => Auth.SignOutAsync(ct);

    public Task<IReadOnlyList<GoogleTaskList>> GetListsAsync(CancellationToken ct = default) => Client.ListListsAsync(ct);

    // Opent de inlogpagina van Google in de standaardbrowser, waar je zelf je wachtwoord invult.
    private static Task OpenBrowser(Uri url)
    {
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Provider.Dispose();
        _http.Dispose();
    }
}

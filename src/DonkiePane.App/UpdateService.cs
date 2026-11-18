using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using DonkiePane.Core.Updates;

namespace DonkiePane.App;

/// <summary>Uitkomst van één controle op een nieuwe versie.</summary>
internal enum UpdateCheckOutcome { UpToDate, Available, NoReleases, Failed }

/// <summary>
/// Kijkt op GitHub of er een nieuwere DonkiePane is, downloadt op verzoek de installer en start die. Installeren
/// gebeurt alleen na een klik van de gebruiker; deze klasse plant zelf niets in (dat doet <c>App.Updates</c>).
/// </summary>
internal sealed class UpdateService : IDisposable
{
    private readonly ReleaseFeed _feed;
    private readonly HttpClient _api;
    private readonly HttpClient _download;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _checking = new(1, 1);

    public UpdateService(Version current, HttpMessageHandler? handler = null, TimeProvider? time = null)
    {
        Current = current;
        _time = time ?? TimeProvider.System;
        _api = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _api.Timeout = TimeSpan.FromSeconds(30);
        // Een installer is zo'n 50 MB; op een trage verbinding duurt dat even.
        _download = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _download.Timeout = TimeSpan.FromMinutes(15);
        _feed = new ReleaseFeed(_api);
    }

    /// <summary>De versie die nu draait.</summary>
    public Version Current { get; }

    /// <summary>Een nieuwere versie, als de laatste controle die vond.</summary>
    public ReleaseInfo? Available { get; private set; }

    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>Waarom de laatste controle mislukte, of <c>null</c>.</summary>
    public string? LastError { get; private set; }

    public Uri ReleasesPage => _feed.ReleasesPage;

    /// <summary>Na elke controle (ook een mislukte).</summary>
    public event EventHandler? Checked;

    /// <summary>De versie van dit programma, uit de assembly (die komt uit <c>Directory.Build.props</c>).</summary>
    public static Version RunningVersion() =>
        AppVersion.TryParse(typeof(UpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? typeof(UpdateService).Assembly.GetName().Version
        ?? new Version(0, 0, 0);

    public async Task<UpdateCheckOutcome> CheckAsync(CancellationToken ct = default)
    {
        await _checking.WaitAsync(ct); // de knop in Instellingen en de klok tegelijk: één verzoek tegelijk
        try
        {
            UpdateCheckOutcome outcome;
            try
            {
                var latest = await _feed.GetLatestAsync(ct);
                Available = latest is not null && AppVersion.IsNewer(latest.Version, Current) ? latest : null;
                LastError = null;
                outcome = latest is null ? UpdateCheckOutcome.NoReleases
                    : Available is not null ? UpdateCheckOutcome.Available : UpdateCheckOutcome.UpToDate;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                // Geen internet of GitHub hapert: de vorige uitkomst blijft staan, volgende keer opnieuw.
                LastError = ex is TaskCanceledException && !ct.IsCancellationRequested
                    ? "GitHub reageerde niet op tijd." : "Geen verbinding met GitHub.";
                outcome = UpdateCheckOutcome.Failed;
            }
            LastChecked = _time.GetLocalNow();
            Checked?.Invoke(this, EventArgs.Empty);
            return outcome;
        }
        finally
        {
            _checking.Release();
        }
    }

    /// <summary>De map waarin de installer wordt gedownload.</summary>
    public static string DownloadFolder => Path.Combine(Path.GetTempPath(), "DonkiePane-update");

    /// <summary>Downloadt en controleert de installer van <paramref name="release"/>.</summary>
    /// <exception cref="UpdateException">Met een tekst voor de gebruiker.</exception>
    public async Task<string> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct)
    {
        if (release.Installer is not { } installer)
            throw new UpdateException("Bij deze versie staat geen installer. Kijk op de releasepagina.");
        try
        {
            return await InstallerDownload.DownloadAsync(_download, installer, DownloadFolder, progress, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("Het downloaden is mislukt: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new UpdateException("Het downloaden duurde te lang. Probeer het later opnieuw.", ex);
        }
    }

    /// <summary>
    /// De argumenten voor de installer: zonder vragen (wel met voortgang), geen herstart van Windows, en daarna
    /// DonkiePane weer starten. De installer vraagt deze DonkiePane zelf netjes af te sluiten (<c>--quit</c>).
    /// </summary>
    internal const string InstallerArguments = "/SILENT /NORESTART /STARTAFTER=1";

    public static void LaunchInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, InstallerArguments) { UseShellExecute = true });

    public void Dispose()
    {
        _api.Dispose();
        _download.Dispose();
        _checking.Dispose();
    }
}

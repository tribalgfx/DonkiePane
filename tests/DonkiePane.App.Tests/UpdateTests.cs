using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Controls;
using DonkiePane.App;
using DonkiePane.Core;
using DonkiePane.Core.Updates;

namespace DonkiePane.App.Tests;

/// <summary>Een nagebootste GitHub die steeds hetzelfde antwoordt.</summary>
internal sealed class FakeReleases(HttpStatusCode status, string json) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    public static FakeReleases Latest(string tag) => new(HttpStatusCode.OK, $$"""
        {
          "tag_name": "{{tag}}", "html_url": "https://github.com/tribalgfx/DonkiePane/releases/tag/{{tag}}",
          "draft": false, "prerelease": false, "body": "- **Nieuw:** updates",
          "assets": [{ "name": "DonkiePane-Setup-{{tag[1..]}}.exe", "size": 3,
                       "browser_download_url": "https://github.com/tribalgfx/DonkiePane/releases/download/{{tag}}/DonkiePane-Setup-{{tag[1..]}}.exe",
                       "digest": "sha256:{{new string('a', 64)}}" }]
        }
        """);
}

public sealed class UpdateServiceTests
{
    [Fact]
    public async Task A_newer_release_becomes_available()
    {
        using var service = new UpdateService(new Version(0, 1, 0), FakeReleases.Latest("v0.2.0"));

        var outcome = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.Available, outcome);
        Assert.Equal(new Version(0, 2, 0, 0), service.Available!.Version);
        Assert.NotNull(service.LastChecked);
        Assert.Null(service.LastError);
    }

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("v0.0.9")]
    public async Task The_same_or_an_older_release_is_not_offered(string tag)
    {
        using var service = new UpdateService(new Version(0, 1, 0), FakeReleases.Latest(tag));

        Assert.Equal(UpdateCheckOutcome.UpToDate, await service.CheckAsync());
        Assert.Null(service.Available);
    }

    [Fact]
    public async Task No_releases_is_not_an_error()
    {
        using var service = new UpdateService(new Version(0, 1, 0), new FakeReleases(HttpStatusCode.NotFound, "{}"));

        Assert.Equal(UpdateCheckOutcome.NoReleases, await service.CheckAsync());
        Assert.Null(service.LastError);
    }

    [Fact]
    public async Task A_failed_check_keeps_the_update_found_earlier_and_says_why()
    {
        var handler = new SwitchingHandler(FakeReleases.Latest("v0.2.0"));
        using var service = new UpdateService(new Version(0, 1, 0), handler);
        await service.CheckAsync();

        handler.Inner = new FakeReleases(HttpStatusCode.BadGateway, "{}");
        var outcome = await service.CheckAsync();

        Assert.Equal(UpdateCheckOutcome.Failed, outcome);
        Assert.NotNull(service.Available);
        Assert.NotNull(service.LastError);
    }

    [Fact]
    public void The_running_version_comes_from_the_build() =>
        Assert.True(UpdateService.RunningVersion() > new Version(0, 0, 0));

    [Fact]
    public void The_installer_runs_silently_and_restarts_DonkiePane_afterwards()
    {
        Assert.Contains("/SILENT", UpdateService.InstallerArguments);
        Assert.Contains("/STARTAFTER=1", UpdateService.InstallerArguments); // zie installer\DonkiePane.iss, StartAfterUpdate
    }

    [Fact]
    public void Release_notes_are_shown_without_markdown()
    {
        var plain = UpdateWindow.PlainNotes("## Nieuw\r\n- **Updates** via `GitHub`, zie [de README](https://example.com).\n* *schuin*");

        Assert.Equal("Nieuw\n• Updates via GitHub, zie de README.\n• schuin", plain);
    }

    private sealed class SwitchingHandler(HttpMessageHandler inner) : HttpMessageHandler
    {
        public HttpMessageHandler Inner { get; set; } = inner;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            new HttpMessageInvoker(Inner, disposeHandler: false).SendAsync(request, ct);
    }
}

/// <summary>De vensters echt opbouwen, zodat een fout daarin niet pas bij de gebruiker opvalt.</summary>
[Collection("WPF-vensters")] // zie SettingsWindowTests
public sealed class UpdateWindowTests : TempData
{
    private static void OnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("Fout op de UI-thread: " + error, error);
    }

    private static ReleaseInfo Release(string? sha) => new(new Version(0, 2, 0), new Uri("https://github.com/tribalgfx/DonkiePane/releases/tag/v0.2.0"),
        "**Nieuw**", new ReleaseAsset("DonkiePane-Setup-0.2.0.exe",
            new Uri("https://github.com/tribalgfx/DonkiePane/releases/download/v0.2.0/DonkiePane-Setup-0.2.0.exe"), 3, sha));

    [Fact]
    public void The_update_window_shows_both_versions_and_can_install()
    {
        OnStaThread(() =>
        {
            var window = new UpdateWindow(Release(new string('a', 64)), new Version(0, 1, 0),
                (_, _, _) => Task.FromResult(""), _ => { });

            Assert.Contains("0.2.0", ((TextBlock)window.FindName("Headline")).Text);
            Assert.Contains("0.1.0", ((TextBlock)window.FindName("Subline")).Text);
            Assert.Equal("Nieuw", ((TextBlock)window.FindName("Notes")).Text);
            Assert.True(((Button)window.FindName("InstallButton")).IsEnabled);
        });
    }

    [Fact]
    public void Without_a_checksum_the_window_does_not_offer_to_install()
    {
        OnStaThread(() =>
        {
            var window = new UpdateWindow(Release(sha: null), new Version(0, 1, 0), (_, _, _) => Task.FromResult(""), _ => { });

            Assert.False(((Button)window.FindName("InstallButton")).IsEnabled);
            Assert.Contains("controlegetal", ((TextBlock)window.FindName("Status")).Text);
        });
    }

    [Fact]
    public async Task The_settings_window_shows_the_update_setting_and_an_available_update()
    {
        // Eerst controleren, buiten de UI-thread: daar op een await wachten zou vastlopen (geen draaiende Dispatcher).
        using var updates = new UpdateService(new Version(0, 1, 0), FakeReleases.Latest("v0.2.0"));
        await updates.CheckAsync();

        OnStaThread(() =>
        {
            var app = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
            if (!app.Resources.Contains(typeof(CheckBox))) app.Resources[typeof(CheckBox)] = new System.Windows.Style(typeof(CheckBox));
            using var host = new TaskSourceHost(new DonkiePane.Tasks.Plugins.SourcesStore(SourcesPath), new DonkiePane.Tasks.Plugins.PluginLoadResult(),
                id => new DpapiSecretStore(id, SecretsDir), _ => Task.CompletedTask, new FakeLocal(), QueueDir);
            var layout = new Layout { CheckForUpdates = false };
            var shown = 0;

            var window = new SettingsWindow(layout, () => { }, () => { }, host, updates, () => shown++);

            Assert.False(((CheckBox)window.FindName("UpdatesBox")).IsChecked);
            Assert.Equal("Versie 0.2.0 is beschikbaar.", ((TextBlock)window.FindName("UpdateStatus")).Text);
            Assert.Equal(System.Windows.Visibility.Visible, ((Button)window.FindName("ShowUpdateButton")).Visibility);
            Assert.False(layout.CheckForUpdates); // openen verandert niets
        });
    }
}

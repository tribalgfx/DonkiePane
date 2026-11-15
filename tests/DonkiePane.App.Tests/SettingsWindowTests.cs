using System.Windows;
using System.Windows.Controls;
using DonkiePane.App;
using DonkiePane.Core;

namespace DonkiePane.App.Tests;

/// <summary>
/// Het instellingenvenster wordt pas gebouwd als je het opent. Een fout in het opbouwen (bijvoorbeeld een schuifje dat
/// al een gebeurtenis afvuurt voordat de rest bestaat) zou dan de hele app laten crashen. Deze test bouwt het echt op.
/// </summary>
public sealed class SettingsWindowTests : TempData
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

    private TaskSourceHost NewHost() =>
        new(new DonkiePane.Tasks.Plugins.SourcesStore(SourcesPath), new DonkiePane.Tasks.Plugins.PluginLoadResult(),
            id => new DpapiSecretStore(id, SecretsDir), _ => Task.CompletedTask, new FakeLocal(), QueueDir);

    /// <summary>De app levert een standaardstijl voor vinkjes mee; het instellingenvenster bouwt daarop verder.</summary>
    private static void EnsureApplicationWithCheckBoxStyle()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (!app.Resources.Contains(typeof(CheckBox))) app.Resources[typeof(CheckBox)] = new Style(typeof(CheckBox));
    }

    [Fact]
    public void The_window_builds_and_shows_the_current_settings()
    {
        OnStaThread(() =>
        {
            EnsureApplicationWithCheckBoxStyle();
            using var host = NewHost();
            var layout = new Layout { SyncSeconds = 90, GridSize = 48, SnapToGrid = false, HideDesktopIcons = false };

            var window = new SettingsWindow(layout, () => { }, () => { }, host);

            Assert.Equal(90, window.FindName("SyncSlider") is Slider s ? s.Value : -1);
            Assert.Equal("1,5 min", ((TextBlock)window.FindName("SyncValue")).Text.Replace('.', ','));
            Assert.Equal(48, ((Slider)window.FindName("GridSlider")).Value);
            Assert.True(((CheckBox)window.FindName("DoubleClickBox")).IsChecked);
            Assert.False(((CheckBox)window.FindName("SnapBox")).IsChecked);
            Assert.False(((CheckBox)window.FindName("HideIconsBox")).IsChecked);
        });
    }

    [Fact]
    public void Opening_the_window_does_not_change_any_setting_because_the_sliders_fire_while_it_builds()
    {
        OnStaThread(() =>
        {
            EnsureApplicationWithCheckBoxStyle();
            using var host = NewHost();
            var saved = 0;
            var layout = new Layout { SyncSeconds = 120, GridSize = 24 };

            _ = new SettingsWindow(layout, () => saved++, () => { }, host);

            Assert.Equal(0, saved);                                    // niets bewaard
            Assert.Equal(120, layout.SyncSeconds);                     // niets overschreven door het opbouwen
            Assert.Equal(24, layout.GridSize);
            Assert.Equal(TimeSpan.FromSeconds(60), host.PollInterval); // en de bronnen kregen geen ander interval
        });
    }

    [Fact]
    public void Moving_the_sync_slider_saves_and_reaches_the_sources()
    {
        OnStaThread(() =>
        {
            EnsureApplicationWithCheckBoxStyle();
            using var host = NewHost();
            var saved = 0;
            var layout = new Layout();
            var window = new SettingsWindow(layout, () => saved++, () => { }, host);

            ((Slider)window.FindName("SyncSlider")).Value = 30;

            Assert.Equal(30, layout.SyncSeconds);
            Assert.Equal(TimeSpan.FromSeconds(30), host.PollInterval);
            Assert.True(saved >= 1);
            Assert.Equal("30 s", ((TextBlock)window.FindName("SyncValue")).Text);
        });
    }
}

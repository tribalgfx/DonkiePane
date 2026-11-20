using System.Windows.Threading;
using DonkiePane.Core.Updates;
using Forms = System.Windows.Forms;

namespace DonkiePane.App;

/// <summary>
/// Controleren op een nieuwe versie: kort na het starten en daarna eens per dag (uit te zetten in Instellingen).
/// Is er een, dan komt er één melding bij het tray-icoon en een menu-item "Bijwerken naar …". Installeren gebeurt
/// alleen als de gebruiker daar zelf op klikt.
/// </summary>
public partial class App
{
    /// <summary>Niet meteen bij het starten: dan heeft het bureaublad voorrang (en is er bij inloggen vaak nog geen netwerk).</summary>
    private static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromMinutes(1);

    /// <summary>Zo vaak wordt er opnieuw gekeken. De klok tikt elk uur; na een mislukte controle volgt de volgende tik opnieuw.</summary>
    internal static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromDays(1);

    private UpdateService? _updates;
    private DispatcherTimer? _updateTimer;
    private Forms.ToolStripMenuItem? _updateMenuItem;
    private Forms.ToolStripSeparator? _updateMenuSeparator;
    private UpdateWindow? _updateWindow;

    private void StartUpdateChecks()
    {
        _updates = new UpdateService(UpdateService.RunningVersion());
        _updates.Checked += (_, _) => Dispatcher.InvokeAsync(OnUpdateChecked);

        _updateTimer = new DispatcherTimer { Interval = FirstUpdateCheckDelay };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(1);
            if (!_layout.CheckForUpdates) return; // de instelling wordt hier gelezen, zodat een wijziging direct geldt
            if (_updates.LastError is null && _updates.LastChecked is { } last && DateTimeOffset.Now - last < UpdateCheckInterval) return;
            await _updates.CheckAsync();
        };
        _updateTimer.Start();
    }

    /// <summary>Bovenaan het tray-menu, alleen zichtbaar als er een nieuwe versie is.</summary>
    private void AddUpdateMenuItem(Forms.ContextMenuStrip menu)
    {
        _updateMenuItem = new Forms.ToolStripMenuItem(Loc.T("Bijwerken…")) { Visible = false };
        _updateMenuItem.Font = new System.Drawing.Font(_updateMenuItem.Font, System.Drawing.FontStyle.Bold);
        _updateMenuItem.Click += (_, _) => ShowUpdateWindow();
        _updateMenuSeparator = new Forms.ToolStripSeparator { Visible = false };
        menu.Items.Add(_updateMenuItem);
        menu.Items.Add(_updateMenuSeparator);
    }

    private void OnUpdateChecked()
    {
        var release = _updates?.Available;
        var version = release is null ? null : AppVersion.Format(release.Version);
        if (_updateMenuItem is not null && _updateMenuSeparator is not null)
        {
            _updateMenuItem.Text = Loc.F("Bijwerken naar versie {0}…", version);
            _updateMenuItem.Visible = _updateMenuSeparator.Visible = release is not null;
        }
        if (_trayIcon is null) return;
        _trayIcon.Text = release is null ? "DonkiePane" : Loc.F("DonkiePane – versie {0} is beschikbaar", version);

        // Eén melding per nieuwe versie; daarna blijft alleen het menu-item staan.
        if (version is null || _layout.NotifiedUpdateVersion == version) return;
        _layout.NotifiedUpdateVersion = version;
        SaveLayout();
        _balloonIsUpdate = true;
        _trayIcon.ShowBalloonTip(10_000, Loc.T("Nieuwe versie van DonkiePane"),
            Loc.F("Versie {0} is beschikbaar. Klik hier om bij te werken.", version), Forms.ToolTipIcon.Info);
    }

    private void ShowUpdateWindow()
    {
        if (_updates?.Available is not { } release) return;
        if (_updateWindow is not null) // ook als hij net geopend en nog niet geladen is
        {
            _updateWindow.Activate();
            return;
        }

        _updateWindow = new UpdateWindow(release, _updates.Current, _updates.DownloadAsync, _updates.LaunchInstaller);
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        _updateWindow.Show();
        _updateWindow.Activate();
    }

    private void StopUpdateChecks()
    {
        _updateTimer?.Stop();
        _updateWindow?.Close();
        _updates?.Dispose();
    }
}

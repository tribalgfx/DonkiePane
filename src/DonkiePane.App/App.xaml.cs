using System.Windows;
using System.Windows.Threading;
using DonkiePane.Core;
using DonkiePane.Shell;
using Forms = System.Windows.Forms;

namespace DonkiePane.App;

public partial class App : Application
{
    private Forms.NotifyIcon? _trayIcon;
    private ShellRestartWatcher? _shellWatcher;
    private readonly LayoutStore _store = new(LayoutStore.DefaultPath);
    private Layout _layout = new();
    private readonly List<PaneWindow> _windows = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _layout = _store.Load();
        if (_layout.Panes.Count == 0)
            _layout.Panes.Add(new Pane { Title = "Welkom bij DonkiePane" });

        foreach (var pane in _layout.Panes)
            ShowPane(pane);

        _trayIcon = CreateTrayIcon();

        _shellWatcher = new ShellRestartWatcher();
        _shellWatcher.ShellRestarted += (_, _) => RebuildPanesSoon();
    }

    /// <summary>
    /// Na een herstart van Verkenner zijn de panes met het oude bureaublad
    /// meegesloten. Even wachten tot het nieuwe bureaublad klaarstaat en ze
    /// dan opnieuw opbouwen vanuit de layout.
    /// </summary>
    private void RebuildPanesSoon()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var window in _windows.ToList())
                window.Close();
            _windows.Clear();
            foreach (var pane in _layout.Panes)
                ShowPane(pane);
        };
        timer.Start();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Nieuw pane", null, (_, _) => AddPane());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (_, _) => Shutdown());

        return new Forms.NotifyIcon
        {
            Icon = TrayIconFactory.CreatePlaidIcon(),
            Text = "DonkiePane",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    private void AddPane()
    {
        var pane = new Pane { Left = 140 + 30 * _layout.Panes.Count, Top = 140 + 30 * _layout.Panes.Count };
        _layout.Panes.Add(pane);
        ShowPane(pane);
        SaveLayout();
    }

    private void ShowPane(Pane pane)
    {
        var window = new PaneWindow(pane);
        window.PaneChanged += (_, _) => SaveLayout();
        window.PaneDeleted += (_, _) =>
        {
            _layout.Panes.Remove(pane);
            _windows.Remove(window);
            SaveLayout();
        };
        _windows.Add(window);
        window.Show();
    }

    private void SaveLayout() => _store.Save(_layout);

    protected override void OnExit(ExitEventArgs e)
    {
        SaveLayout();
        _shellWatcher?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.OnExit(e);
    }
}

using System.IO;
using System.Windows;
using System.Windows.Threading;
using DonkiePane.Core;
using DonkiePane.Shell;
using DonkiePane.Tasks;
using Forms = System.Windows.Forms;

namespace DonkiePane.App;

public partial class App : Application
{
    private const int IconPixelSize = 96; // scherp tot 200% schaling bij 48 DIP

    private readonly LayoutStore _store = new(LayoutStore.DefaultPath);
    private readonly List<PaneWindow> _windows = [];
    private readonly Dictionary<Pane, IconsView> _iconViews = [];
    private readonly ITaskProvider _localTasks = new LocalTaskProvider(new TaskStore(TaskStore.DefaultPath));
    private readonly Dictionary<string, ItemView> _itemCache = new(StringComparer.OrdinalIgnoreCase);
    private Layout _layout = new();
    private DesktopFolders? _desktop;
    private DispatcherTimer? _resyncTimer;
    private DispatcherTimer? _watchTimer;
    private bool _balloonIsUpdate;   // waar een klik op de melding bij het icoon heen gaat
    private bool _saveFailureShown;  // één melding per reeks mislukte keren opslaan
    private bool _exited;            // OnExit is geweest (kan al tijdens het welkomstscherm)
    private Forms.NotifyIcon? _trayIcon;
    private ShellRestartWatcher? _shellWatcher;
    private RecycleBinWatcher? _recycleBinWatcher;
    private GridOverlayWindow? _gridOverlay;
    private Mutex? _singleInstance;
    private bool _isDuplicate;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Maar één DonkiePane tegelijk: twee zouden elkaars panes en de
        // Windows-iconen door elkaar halen.
        var options = StartupOptions.Parse(e.Args);
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\DonkiePane.SingleInstance", out var isFirst);
        if (!isFirst || options.Quit)
        {
            // --quit: een draaiende DonkiePane netjes laten afsluiten (de installer en het verwijderprogramma doen dat).
            // Draait er niets, dan is er niets te doen en start dit proces ook niets op.
            if (options.Quit && !isFirst) RequestRunningInstanceToQuit();
            _isDuplicate = true;
            Shutdown();
            return;
        }

        ListenForQuitRequest();
        InstallCrashSafety();
        RepairAutoStart();
        WaitForDesktop();

        // Ontbreekt alleen het hoofdbestand maar is er een back-up, dan kent deze gebruiker DonkiePane al.
        var firstRun = !File.Exists(LayoutStore.DefaultPath) && !File.Exists(LayoutStore.DefaultPath + ".bak");
        try
        {
            _layout = _store.Load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leeg beginnen zou de indeling bij de eerste keer opslaan overschrijven. Niets veranderd aan het bureaublad.
            MessageBox.Show($"DonkiePane kan je indeling niet lezen en start daarom niet.\n\n{ex.Message}\n\n" +
                "Probeer het over een momentje opnieuw.", "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
            _isDuplicate = true; // niets opgezet, dus bij afsluiten ook niets terugdraaien
            Shutdown();
            return;
        }
        if (firstRun) _layout.WelcomeShown = false; // oude layouts hebben deze waarde niet: die gebruikers kennen DonkiePane al
        LayoutEditor.Repair(_layout); // een kapot of met de hand aangepast bestand mag het starten niet laten mislukken
        _sources = CreateSourceHost();
        _sources.SetPollInterval(_layout.SyncInterval);
        if (LegacyGoogleMigration.Run(_sources, _layout)) SaveLayout();
        _desktop = new DesktopFolders();
        var unavailable = new List<string>();
        LayoutEditor.Sync(_layout, _desktop.EnumerateItems(unavailable), unavailable);
        SaveLayout();

        foreach (var pane in _layout.Panes)
            ShowPane(pane);
        RefreshItems();
        var openSettingsAfterWelcome = ShowWelcomeIfFirstRun(); // vóór het verbergen van de Windows-iconen
        // Kwam er tijdens het welkomstscherm een --quit binnen (bijv. van de installer), dan is OnExit al geweest:
        // nu nog iconen verbergen of een tray-icoon maken zou blijven hangen zonder dat DonkiePane draait.
        if (_exited) return;
        ApplyDesktopIconsSetting();
        EnsurePanesVisible();
        WatchDisplays();
        WatchDesktopDoubleClick();

        _resyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _resyncTimer.Tick += (_, _) =>
        {
            _resyncTimer.Stop();
            Resync();
        };
        _desktop.Changed += (_, _) => Dispatcher.InvokeAsync(ScheduleResync);
        _desktop.Renamed += (_, ids) => Dispatcher.InvokeAsync(() =>
        {
            _itemCache.Remove(ids.OldId);
            LayoutEditor.Rename(_layout, ids.OldId, ids.NewId);
        });
        _desktop.StartWatching();
        // Een bureaubladmap die bij het starten nog niet bereikbaar was, of een watcher die na een fout is gestopt,
        // wordt zo alsnog gevolgd; dan meteen opnieuw inlezen.
        _watchTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _watchTimer.Tick += (_, _) => { if (_desktop.StartWatching()) ScheduleResync(); };
        _watchTimer.Start();
        StartRecycleBinWatcher();

        _trayIcon = CreateTrayIcon();
        StartUpdateChecks();
        if (options.OpenSettings || openSettingsAfterWelcome) ShowSettings(); // --settings is handig voor een snelkoppeling

        _shellWatcher = new ShellRestartWatcher();
        _shellWatcher.ShellRestarted += (_, _) => RebuildPanesSoon();
    }

    /// <summary>Zet "starten met Windows" van een oudere versie recht (zie <see cref="AutoStart.RepairApproval"/>).</summary>
    private static void RepairAutoStart()
    {
        try { AutoStart.RepairApproval(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
    }

    /// <summary>
    /// Bij het inloggen start DonkiePane soms eerder dan het bureaublad van
    /// Verkenner. Zonder bureaublad kunnen de panes er niet aan hangen, dus
    /// even wachten (maximaal 30 seconden; daarna gewoon doorgaan).
    /// </summary>
    private static void WaitForDesktop()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!DesktopIcons.IsDesktopReady() && DateTime.UtcNow < deadline)
            Thread.Sleep(250);
    }

    // ---------- Bureaubladitems ----------

    private void ScheduleResync()
    {
        // Meerdere wijzigingen kort na elkaar (bijv. een installatie) samen verwerken.
        _resyncTimer!.Stop();
        _resyncTimer.Start();
    }

    private void Resync()
    {
        var unavailable = new List<string>();
        var items = _desktop!.EnumerateItems(unavailable);
        foreach (var stale in _itemCache.Keys.Except(items, StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (unavailable.Any(location => stale.StartsWith(location + "/", StringComparison.OrdinalIgnoreCase))) continue; // icoon houden
            _itemCache.Remove(stale);
        }

        // Een map die even weg is (netwerk, OneDrive) wist de indeling niet: die items blijven staan.
        LayoutEditor.Sync(_layout, items, unavailable);
        _desktop.StartWatching(); // na een fout van de watcher: opnieuw volgen
        SaveLayout();
        RefreshItems();
    }

    private void RefreshItems()
    {
        foreach (var (pane, view) in _iconViews)
            view.SetItems(pane.Items.Select(GetItemView).OfType<ItemView>().ToList());
    }

    /// <summary>De Prullenbak is veranderd: icoon (vol/leeg) en naam van de systeemiconen opnieuw ophalen.</summary>
    private void RefreshVirtualItems()
    {
        foreach (var id in _itemCache.Where(entry => VirtualDesktopItems.IsVirtual(entry.Value.Path)).Select(entry => entry.Key).ToList())
            _itemCache.Remove(id);
        RefreshItems();
    }

    private ItemView? GetItemView(string id)
    {
        if (_itemCache.TryGetValue(id, out var cached)) return cached;
        var path = _desktop!.ResolvePath(id);
        if (path is null) return null;

        var view = new ItemView(id, path, ShellItemInfo.GetDisplayName(path), ShellItemInfo.GetIcon(path, IconPixelSize));
        _itemCache[id] = view;
        return view;
    }

    // ---------- Panes ----------

    private void AddPane(Pane pane)
    {
        if (_panesHidden) ShowAllPanes(); // een nieuw pane moet je zien
        var offset = 30 * (_layout.Panes.Count % 10);
        pane.Left = 500 + offset;
        pane.Top = 140 + offset;
        _layout.Panes.Add(pane);
        ShowPane(pane);
        RefreshItems();
        SaveLayout();
    }

    private void ShowPane(Pane pane)
    {
        var window = new PaneWindow(pane);
        window.GridSettings = () => (_layout.SnapToGrid, _layout.GridSize);
        window.SizeMoveStarted += (_, _) =>
        {
            if (!_layout.SnapToGrid) return;
            _gridOverlay ??= new GridOverlayWindow();
            _gridOverlay.ShowFor(window, _layout.GridSize);
        };
        window.SizeMoveEnded += (_, _) =>
        {
            _gridOverlay?.Hide();
            ClearHome(pane); // zelf verplaatst: niet meer terugzetten naar de oude plek
            pane.Monitor = MonitorOf(window) ?? pane.Monitor;
        };
        window.PaneChanged += (_, _) => SaveLayout();
        window.DeleteRequested += async (_, _) => await DeletePaneAsync(window);

        if (pane.Kind == PaneKind.Tasks)
        {
            pane.TaskListId ??= Guid.NewGuid().ToString("N");
            window.SetContent(new TasksView(TaskProviderFor(pane), pane.TaskListId));
        }
        else
        {
            var view = new IconsView();
            view.ItemDropped += (_, drop) =>
            {
                if (!LayoutEditor.MoveItem(_layout, drop.ItemId, pane.Id, drop.Index)) return;
                SaveLayout();
                RefreshItems();
            };
            _iconViews[pane] = view;
            window.SetContent(view);
        }

        _windows.Add(window);
        window.Show();
    }

    private async Task DeletePaneAsync(PaneWindow window)
    {
        var pane = window.Pane;
        if (pane.Kind == PaneKind.Tasks && pane.TaskListId is { } listId)
        {
            var provider = TaskProviderFor(pane);
            try
            {
                // Lokale taken staan alleen in dit pane en zouden echt weg zijn: eerst vragen.
                // Een Google-lijst blijft bij Google staan, dus daar valt niets te verliezen.
                if (provider.Capabilities.PaneOwnsTasks)
                {
                    var count = (await provider.GetTasksAsync(listId)).Count;
                    if (count > 0)
                    {
                        var answer = MessageBox.Show(
                            $"Pane \"{pane.Title}\" verwijderen? De {count} taken erin worden ook verwijderd.",
                            "DonkiePane", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                        if (answer != MessageBoxResult.Yes) return;
                    }
                }
                await provider.DeleteListAsync(listId);
            }
            catch (Exception ex) when (TaskErrors.IsHandled(ex))
            {
                // Aangeroepen vanuit een async void-handler: een fout hier mag de app niet laten crashen.
                MessageBox.Show($"Pane \"{pane.Title}\" is niet verwijderd. {TaskErrors.Message(ex)}",
                    "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (!LayoutEditor.DeletePane(_layout, pane.Id)) return;
        _windows.Remove(window);
        _iconViews.Remove(pane);
        window.Close();
        SaveLayout();
        RefreshItems();
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
            _panesHidden = false; // de nieuwe panes zijn zichtbaar
            _iconViews.Clear();
            _gridOverlay?.Close(); // hing aan het oude bureaublad
            _gridOverlay = null;
            foreach (var pane in _layout.Panes)
                ShowPane(pane);
            StartRecycleBinWatcher(); // de aanmelding bij de oude Verkenner geldt niet meer
            RefreshItems();
            ApplyDesktopIconsSetting();
            EnsurePanesVisible();
        };
        timer.Start();
    }

    /// <summary>Volgt of de Prullenbak vol of leeg is (voor het icoon); na een herstart van Verkenner opnieuw.</summary>
    private void StartRecycleBinWatcher()
    {
        _recycleBinWatcher?.Dispose();
        _recycleBinWatcher = new RecycleBinWatcher();
        _recycleBinWatcher.Changed += (_, _) => RefreshVirtualItems();
    }

    /// <summary>
    /// Bewaart de indeling. Lukt dat niet (schijf vol, bestand vergrendeld), dan blijft alles in het geheugen staan
    /// en probeert de volgende keer opslaan het opnieuw; de gebruiker krijgt één melding in plaats van een crash.
    /// </summary>
    private void SaveLayout()
    {
        try
        {
            _store.Save(_layout);
            _saveFailureShown = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_saveFailureShown || _trayIcon is null) return;
            _saveFailureShown = true;
            _balloonIsUpdate = false;
            _trayIcon.ShowBalloonTip(10_000, "DonkiePane kon je indeling niet opslaan",
                $"{ex.Message} DonkiePane probeert het bij de volgende wijziging opnieuw.", Forms.ToolTipIcon.Warning);
        }
    }

    // ---------- Windows-iconen ----------

    /// <summary>Bij starten en na een herstart van Verkenner.</summary>
    private void ApplyDesktopIconsSetting()
    {
        if (_layout.HideDesktopIcons) DesktopIcons.SetVisible(false);
    }

    /// <summary>
    /// Bij afsluiten of crash: zolang DonkiePane de iconen beheert, zet het ze
    /// terug. Dat geldt ook als een vorige keer DonkiePane hard is gestopt.
    /// </summary>
    private void RestoreDesktopIcons()
    {
        if (_layout.HideDesktopIcons) DesktopIcons.SetVisible(true);
    }

    /// <summary>Bij een crash de Windows-iconen altijd terugzetten.</summary>
    private void InstallCrashSafety()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            RestoreDesktopIcons();
            WriteCrashLog(e.ExceptionObject as Exception);
        };
        DispatcherUnhandledException += (_, e) =>
        {
            RestoreDesktopIcons();
            WriteCrashLog(e.Exception);
            if (_trayIcon is not null) _trayIcon.Visible = false; // geen spookicoon bij de klok
        };
    }

    /// <summary>
    /// Schrijft wat er misging naar <c>%APPDATA%\DonkiePane\crash.log</c>, zodat een crash achteraf te onderzoeken
    /// is. Het bestand blijft op deze pc; er wordt niets verstuurd. Het wordt nooit groter dan zo'n 256 kB.
    /// </summary>
    private static void WriteCrashLog(Exception? ex)
    {
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(LayoutStore.DefaultPath)!, "crash.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}  DonkiePane {UpdateService.RunningVersion()}{Environment.NewLine}" +
                $"{ex?.ToString() ?? "(onbekende fout)"}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException)
        {
            // Een crashlog mag zelf nooit een tweede fout geven.
        }
    }

    // ---------- Tray ----------

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        AddUpdateMenuItem(menu);
        menu.Items.Add("Nieuw iconenpane", null, (_, _) => AddPane(new Pane()));
        var newTasks = new Forms.ToolStripMenuItem("Nieuw takenpane");
        newTasks.DropDownOpening += (_, _) => FillNewTasksMenu(newTasks);
        FillNewTasksMenu(newTasks);
        menu.Items.Add(newTasks);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Panes verbergen of tonen", null, (_, _) => TogglePanesHidden());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Instellingen…", null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Afsluiten", null, (_, _) => Shutdown());

        var icon = new Forms.NotifyIcon
        {
            Icon = TrayIconFactory.CreateAppIcon(),
            Text = "DonkiePane",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => ShowSettings();
        icon.BalloonTipClicked += (_, _) => { if (_balloonIsUpdate) ShowUpdateWindow(); };
        return icon;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exited = true;

        // Een tweede instantie heeft niets opgezet en mag dus ook niets
        // terugdraaien (anders komen de Windows-iconen terug terwijl de
        // eerste instantie ze verbergt).
        if (_isDuplicate)
        {
            base.OnExit(e);
            return;
        }

        RestoreDesktopIcons(); // eerst: wat er verder ook misgaat, het bureaublad moet terug
        SaveLayout();
        StopUpdateChecks();
        StopWatchingDisplays();
        _watchTimer?.Stop();
        _doubleClickWatcher?.Dispose();
        _sources.Dispose();
        _desktop?.Dispose();
        _shellWatcher?.Dispose();
        _recycleBinWatcher?.Dispose();
        // De mutex bewust níet loslaten: de installer wacht tot hij weg is en vervangt dan de bestanden. Hier is het
        // proces nog niet klaar (DonkiePane.exe en de DLL's zijn nog in gebruik); Windows laat hem los als het proces
        // echt stopt.
        GC.KeepAlive(_singleInstance);
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.OnExit(e);
    }
}


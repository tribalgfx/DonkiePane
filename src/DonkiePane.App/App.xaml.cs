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
    private int _iconLoadPixels = 96; // hoe scherp bureaubladiconen geladen worden (zie IconMetrics.LoadPixels)

    private readonly LayoutStore _store = new(LayoutStore.DefaultPath);
    private readonly List<PaneWindow> _windows = [];
    private readonly Dictionary<Pane, IconsView> _iconViews = [];
    private readonly Dictionary<Pane, FolderPaneView> _folderViews = [];
    private readonly Dictionary<Pane, FrameworkElement> _contents = []; // de inhoud van elk pane, ook van tabbladen die niet open staan
    private readonly ITaskProvider _localTasks = new LocalTaskProvider(new TaskStore(TaskStore.DefaultPath));
    private readonly Dictionary<string, ItemView> _itemCache = new(StringComparer.OrdinalIgnoreCase);
    private Layout _layout = new();
    private DesktopFolders? _desktop;
    private DispatcherTimer? _resyncTimer;
    private DispatcherTimer? _watchTimer;
    private Action? _balloonClicked; // wat een klik op de melding bij het icoon doet
    private bool _saveFailureShown;  // één melding per reeks mislukte keren opslaan
    private bool _exited;            // OnExit is geweest (kan al tijdens het welkomstscherm)
    private Forms.NotifyIcon? _trayIcon;
    private ShellRestartWatcher? _shellWatcher;
    private RecycleBinWatcher? _recycleBinWatcher;
    private GridOverlayWindow? _gridOverlay;
    private readonly List<DesktopDropWindow> _dropWindows = [];

    // Het pane waar iets in geplakt, neergezet of via Nieuw gemaakt is: nieuwe bureaubladitems gaan daarheen.
    private Guid? _arrivalPane;
    private DateTime _arrivalUntil;
    private static readonly TimeSpan ArrivalWindow = TimeSpan.FromSeconds(60);
    private Mutex? _singleInstance;
    private bool _isDuplicate;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Maar één DonkiePane tegelijk: twee zouden elkaars panes en de
        // Windows-iconen door elkaar halen.
        var options = StartupOptions.Parse(e.Args);
        Loc.Use(Loc.Resolve(null)); // tot de indeling gelezen is: de taal van Windows
        if (options.RestartAfter is { } previous) WaitForExit(previous);
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
            MessageBox.Show(Loc.F("DonkiePane kan je indeling niet lezen en start daarom niet.\n\n{0}\n\nProbeer het over een momentje opnieuw.", ex.Message),
                "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
            _isDuplicate = true; // niets opgezet, dus bij afsluiten ook niets terugdraaien
            Shutdown();
            return;
        }
        Loc.Use(Loc.Resolve(_layout.Language)); // vóór er panes, menu's of bronnen zijn
        if (firstRun) _layout.WelcomeShown = false; // oude layouts hebben deze waarde niet: die gebruikers kennen DonkiePane al
        LayoutEditor.Repair(_layout); // een kapot of met de hand aangepast bestand mag het starten niet laten mislukken
        _sources = CreateSourceHost();
        _sources.SetPollInterval(_layout.SyncInterval);
        if (LegacyGoogleMigration.Run(_sources, _layout)) SaveLayout();
        _desktop = new DesktopFolders();
        var unavailable = new List<string>();
        LayoutEditor.Sync(_layout, _desktop.EnumerateItems(unavailable), unavailable, isFolder: IsFolderItem);
        SaveLayout();
        LoadUsage();
        TrackUsage();
        UpdateIconLoadPixels();

        foreach (var pane in _layout.Panes.Where(p => p.TabHost is null).ToList())
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
            _usage.Rename(ids.OldId, ids.NewId);
            SaveUsage();
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
        StartDueReminders();
        StartDimming();
        if (options.OpenSettings || openSettingsAfterWelcome) ShowSettings(); // --settings is handig voor een snelkoppeling

        _shellWatcher = new ShellRestartWatcher();
        _shellWatcher.ShellRestarted += (_, _) => RebuildPanesSoon();

        StartPeekHotkey();
        WatchLookChanges();
    }

    /// <summary>Opnieuw starten: wachten tot de vorige DonkiePane weg is (hooguit 20 seconden).</summary>
    private static void WaitForExit(int processId)
    {
        try
        {
            using var previous = System.Diagnostics.Process.GetProcessById(processId);
            previous.WaitForExit(20_000);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Al weg.
        }
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
        var arrival = _arrivalPane is not null && DateTime.UtcNow < _arrivalUntil ? _arrivalPane : null;
        LayoutEditor.Sync(_layout, items, unavailable, arrival, IsFolderItem);
        _desktop.StartWatching(); // na een fout van de watcher: opnieuw volgen
        SaveLayout();
        TrackUsage();
        RefreshItems();
    }

    private void RefreshItems()
    {
        UpdateIconLoadPixels();
        foreach (var pane in _iconViews.Keys.ToList()) RefreshItems(pane);
    }

    /// <summary>
    /// Bureaubladiconen worden één keer geladen en door alle panes gedeeld: zo scherp als het pane met de grootste
    /// iconen nodig heeft. Verandert dat, dan opnieuw laden.
    /// </summary>
    private void UpdateIconLoadPixels()
    {
        var needed = _layout.Panes.Where(p => p.Kind == PaneKind.Icons)
            .Select(p => IconMetrics.LoadPixels(IconMetrics.Pixels(p))).DefaultIfEmpty(96).Max();
        if (needed == _iconLoadPixels) return;
        _iconLoadPixels = needed;
        _itemCache.Clear();
    }

    private void RefreshItems(Pane pane)
    {
        if (!_iconViews.TryGetValue(pane, out var view)) return;
        var items = pane.Items.Select(GetItemView).OfType<ItemView>().ToList();
        view.SetView(pane.IconSize, sorted: pane.SortBy != ItemSortOrder.Manual, pane.IconPixels, pane.ShowNames);
        view.SetItems(pane.SortBy == ItemSortOrder.Manual ? items : Sorted(items, pane.SortBy));
        UpdateDimming(pane);
    }

    /// <summary>Voor de regel "is een map": staat dit bureaubladitem voor een map?</summary>
    private bool IsFolderItem(string id) => _desktop?.ResolvePath(id) is { } path && Directory.Exists(path);

    /// <summary>De iconen gesorteerd op naam, type of datum; de eigen volgorde in het pane blijft bewaard.</summary>
    private static List<ItemView> Sorted(List<ItemView> items, ItemSortOrder order)
    {
        var byId = new Dictionary<string, ItemView>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items) byId.TryAdd(item.Id, item);
        var sortable = byId.Values.Select(i =>
        {
            var isVirtual = VirtualDesktopItems.IsVirtual(i.Path);
            var isFolder = isVirtual || Directory.Exists(i.Path);
            DateTime modified;
            try { modified = isVirtual ? DateTime.MinValue : isFolder ? Directory.GetLastWriteTimeUtc(i.Path) : File.GetLastWriteTimeUtc(i.Path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { modified = DateTime.MinValue; }
            var extension = isFolder ? "" : Path.GetExtension(i.Path).TrimStart('.');
            return new SortableItem(i.Id, i.DisplayName, isFolder, extension, modified);
        });
        return ItemSort.Order(sortable, order).Select(id => byId[id]).ToList();
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

        var view = new ItemView(id, path, ShellItemInfo.GetDisplayName(path), ShellItemInfo.GetIcon(path, _iconLoadPixels));
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

    /// <summary>Een venster voor dit pane met zijn tabbladen (alleen voor panes die geen tabblad in een ander venster zijn).</summary>
    private PaneWindow ShowPane(Pane pane)
    {
        var window = new PaneWindow(pane);
        window.GridSettings = () => (_layout.SnapToGrid, _layout.GridSize);
        window.LookSettings = CurrentPaneStyle;
        window.MergeTargets = () => _windows.Where(w => w != window && !w.IsClosed).Select(w => w.ActivePane).ToList();
        window.SizeMoveStarted += (_, _) =>
        {
            if (!_layout.SnapToGrid) return;
            _gridOverlay ??= new GridOverlayWindow();
            _gridOverlay.ShowFor(window, _layout.GridSize);
        };
        window.SizeMoveEnded += (_, _) =>
        {
            _gridOverlay?.Hide();
            ClearHome(window.Pane); // zelf verplaatst: niet meer terugzetten naar de oude plek
            window.Pane.Monitor = MonitorOf(window) ?? window.Pane.Monitor;
        };
        window.PaneChanged += (_, _) => SaveLayout();
        window.ViewChanged += (_, _) =>
        {
            RefreshItems();
            if (_folderViews.TryGetValue(window.ActivePane, out var folder)) folder.Refresh();
        };
        window.DeleteRequested += async (_, _) => await DeletePaneAsync(window);
        window.TabSelected += (_, tab) =>
        {
            if (!LayoutEditor.ActivateTab(_layout, tab.Id)) return;
            RefreshTabs(window);
            SaveLayout();
        };
        window.TabDetachRequested += (_, tab) => DetachTab(tab, followCursor: System.Windows.Forms.Control.MouseButtons == System.Windows.Forms.MouseButtons.Left);
        window.MergeRequested += (_, target) => MergeIntoWindow(window.ActivePane, target, wholeWindow: false);
        window.FolderChangeRequested += (_, _) => ChooseFolder(window.ActivePane);
        window.Moving += (_, cursor) => TrackMergeTarget(window, cursor);
        window.MoveFinished += (_, cursor) => FinishMergeDrag(window, cursor);

        _windows.Add(window);
        RefreshTabs(window);
        window.Show();
        return window;
    }

    /// <summary>De inhoud van een pane (iconen, taken of een map); gemaakt bij het eerste gebruik en daarna bewaard.</summary>
    private FrameworkElement ContentFor(Pane pane)
    {
        if (_contents.TryGetValue(pane, out var existing)) return existing;
        FrameworkElement content;
        switch (pane.Kind)
        {
            case PaneKind.Tasks:
                pane.TaskListId ??= Guid.NewGuid().ToString("N");
                content = new TasksView(TaskProviderFor(pane), pane.TaskListId);
                break;
            case PaneKind.Folder:
                var folder = new FolderPaneView(pane);
                _folderViews[pane] = folder;
                content = folder;
                break;
            default:
                var view = new IconsView();
                view.ItemsDropped += (_, drop) =>
                {
                    // De plek telt tussen de zichtbare iconen; in de indeling kunnen er ook onzichtbare tussen staan.
                    var shown = pane.Items.Where(id => GetItemView(id) is not null).ToList();
                    var index = LayoutEditor.ToItemsIndex(pane.Items, shown, drop.Index);
                    if (!LayoutEditor.MoveItems(_layout, drop.ItemIds, pane.Id, index)) return;
                    SaveLayout();
                    RefreshItems();
                };
                view.ArrivalExpected += (_, _) =>
                {
                    _arrivalPane = pane.Id;
                    _arrivalUntil = DateTime.UtcNow + ArrivalWindow;
                };
                view.DragStarted += (_, _) => ShowDesktopDropWindows();
                view.DragFinished += (_, _) => HideDesktopDropWindows();
                view.ItemOpened += (_, item) => OnItemOpened(pane, item);
                _iconViews[pane] = view;
                content = view;
                RefreshItems(pane);
                break;
        }
        _contents[pane] = content;
        return content;
    }

    /// <summary>Vergeet de inhoud van een pane (verwijderd, of opnieuw op te bouwen).</summary>
    private void ForgetContent(Pane pane)
    {
        if (_contents.Remove(pane, out var content) && content.Parent is System.Windows.Controls.ContentControl owner) owner.Content = null;
        _iconViews.Remove(pane);
        _folderViews.Remove(pane);
    }

    /// <summary>Het venster waarin dit pane staat (als eigen venster of als tabblad).</summary>
    private PaneWindow? WindowOf(Pane pane)
    {
        var host = LayoutEditor.HostOf(_layout, pane);
        return _windows.FirstOrDefault(w => ReferenceEquals(w.Pane, host) && !w.IsClosed);
    }

    /// <summary>Toont de tabbladen van het venster en de inhoud van het open tabblad.</summary>
    private void RefreshTabs(PaneWindow window)
    {
        var tabs = LayoutEditor.TabsOf(_layout, window.Pane);
        var active = LayoutEditor.ActiveTabOf(_layout, window.Pane);
        var content = ContentFor(active);
        // Een inhoud kan maar in één venster tegelijk staan: eerst los uit het venster waar hij nu nog in staat.
        if (!window.Shows(content) && content.Parent is System.Windows.Controls.ContentControl owner) owner.Content = null;
        window.SetTabs(tabs, active, content);
    }

    /// <summary>
    /// Brengt de vensters in lijn met de indeling na samenvoegen, losmaken of verwijderen: één venster per pane dat geen
    /// tabblad is, op de plek uit de indeling, met de juiste tabbladen.
    /// </summary>
    private void SyncWindows()
    {
        var hosts = _layout.Panes.Where(p => p.TabHost is null).ToList();
        foreach (var window in _windows.ToList())
        {
            if (hosts.Contains(window.Pane) && !window.IsClosed) continue;
            _windows.Remove(window);
            window.ReleaseContent();
            window.Close();
        }
        foreach (var pane in _contents.Keys.Except(_layout.Panes).ToList()) ForgetContent(pane);

        // Eerst alle bestaande vensters hun (nieuwe) tabbladen geven, daarna de nieuwe vensters maken.
        foreach (var window in _windows.ToList())
        {
            window.ApplyPaneBounds();
            RefreshTabs(window);
        }
        foreach (var host in hosts.Where(h => _windows.All(w => !ReferenceEquals(w.Pane, h))))
        {
            var window = ShowPane(host);
            if (_panesHidden) window.Hide();
        }
    }

    // ---------- Tabbladen: samenvoegen en losmaken ----------

    private PaneWindow? _mergeTarget;
    private bool _mergeArmed;
    private DispatcherTimer? _mergeTimer;

    /// <summary>
    /// Een pane wordt versleept: staat de muis even (een halve seconde) op de titelbalk van een ander pane, dan licht die
    /// op en wordt het bij loslaten een tabblad daarvan.
    /// </summary>
    private void TrackMergeTarget(PaneWindow moving, System.Drawing.Point cursor)
    {
        var target = _windows.FirstOrDefault(w => w != moving && !w.IsClosed && w.IsVisible && w.TitleBarContains(cursor));
        if (ReferenceEquals(target, _mergeTarget)) return;
        _mergeTarget?.SetMergeHighlight(false);
        _mergeTarget = target;
        _mergeArmed = false;
        _mergeTimer ??= CreateMergeTimer();
        _mergeTimer.Stop();
        if (target is not null) _mergeTimer.Start();
    }

    private DispatcherTimer CreateMergeTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_mergeTarget is null) return;
            _mergeArmed = true;
            _mergeTarget.SetMergeHighlight(true);
        };
        return timer;
    }

    private void FinishMergeDrag(PaneWindow moving, System.Drawing.Point cursor)
    {
        _mergeTimer?.Stop();
        var target = _mergeTarget;
        var armed = _mergeArmed && target is not null && target.TitleBarContains(cursor);
        target?.SetMergeHighlight(false);
        _mergeTarget = null;
        _mergeArmed = false;
        if (!armed) return;
        // Na het afhandelen van het slepen; het versleepte venster verdwijnt.
        Dispatcher.InvokeAsync(() => MergeIntoWindow(moving.Pane, target!.ActivePane, wholeWindow: true));
    }

    /// <summary>
    /// Maakt <paramref name="pane"/> een tabblad in het venster van <paramref name="target"/>. Met
    /// <paramref name="wholeWindow"/> gaan alle tabbladen van zijn venster mee (een heel venster versleept); anders alleen dit tabblad.
    /// </summary>
    private void MergeIntoWindow(Pane pane, Pane target, bool wholeWindow)
    {
        var source = LayoutEditor.HostOf(_layout, pane);
        if (!wholeWindow && LayoutEditor.TabsOf(_layout, source).Count > 1) LayoutEditor.DetachTab(_layout, pane.Id);
        if (LayoutEditor.AddTab(_layout, target.Id, pane.Id) is null) return;
        SyncWindows();
        SaveLayout();
    }

    /// <summary>Haalt een tabblad uit zijn venster; met <paramref name="followCursor"/> hangt het nieuwe venster aan de muis.</summary>
    private void DetachTab(Pane pane, bool followCursor)
    {
        if (LayoutEditor.DetachTab(_layout, pane.Id) is null) return;
        SyncWindows();
        SaveLayout();
        if (!followCursor || WindowOf(pane) is not { } window || DesktopLayer.GetBounds(window) is not { } bounds) return;

        // Het venster onder de muis, met de muis op de titelbalk, en meteen verder slepen.
        var cursor = DesktopLayer.CursorPosition();
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        DesktopLayer.SetBounds(window, cursor.X - (int)(60 * dpi.DpiScaleX), cursor.Y - (int)(16 * dpi.DpiScaleY), bounds.Width, bounds.Height);
        window.Activate();
        DesktopLayer.BeginDragMove(window);
    }

    // ---------- Mappanes ----------

    /// <summary>Een nieuw mappane: eerst een map kiezen.</summary>
    private void AddFolderPane()
    {
        if (PickFolder(null) is { } folder) AddPane(Pane.NewFolderPane(folder));
    }

    private void ChooseFolder(Pane pane)
    {
        if (PickFolder(pane.FolderPath) is not { } folder) return;
        var renamed = pane.FolderPath is null || pane.Title == Pane.NewFolderPane(pane.FolderPath).Title;
        pane.FolderPath = folder;
        if (renamed) pane.Title = Pane.NewFolderPane(folder).Title; // de naam volgde de map: blijf volgen
        if (_folderViews.TryGetValue(pane, out var view)) view.ChangeRoot();
        if (WindowOf(pane) is { } window) RefreshTabs(window);
        SaveLayout();
    }

    private static string? PickFolder(string? start)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Welke map moet dit pane tonen?") };
        if (start is not null && Directory.Exists(start)) dialog.InitialDirectory = start;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    // ---------- Uiterlijk voor alle panes ----------

    private PaneStyle CurrentPaneStyle() =>
        new(PaneThemes.IsLight(_layout.Appearance), _layout.EffectiveCornerRadius, _layout.RollUpOpen);

    /// <summary>Licht/donker, hoeken of oprollen is veranderd: alle panes opnieuw tekenen.</summary>
    private void RefreshAllLooks()
    {
        foreach (var window in _windows.Where(w => !w.IsClosed)) window.RefreshLook();
        UpdateDimming(); // de instelling voor grijze iconen kan veranderd zijn
    }

    // ---------- Iconen op een lege plek van het bureaublad loslaten: nieuw pane ----------

    private void ShowDesktopDropWindows()
    {
        var screens = Forms.Screen.AllScreens;
        while (_dropWindows.Count < screens.Length)
        {
            var window = new DesktopDropWindow();
            window.Dropped += (_, corner) => CreatePaneFromDrop(corner.X, corner.Y);
            _dropWindows.Add(window);
        }
        for (var i = 0; i < _dropWindows.Count; i++)
        {
            if (i < screens.Length) _dropWindows[i].ShowOn(screens[i]);
            else _dropWindows[i].HideNow();
        }
    }

    private void HideDesktopDropWindows()
    {
        foreach (var window in _dropWindows) window.HideNow();
    }

    /// <summary>Een nieuw pane op de plek waar de iconen zijn losgelaten (hoek in schermpixels), met die iconen erin.</summary>
    private void CreatePaneFromDrop(int x, int y)
    {
        if (IconsView.ActiveDrag is not { } drag) return;
        var pane = new Pane { Title = Loc.T("Nieuw pane"), Width = DesktopDropWindow.GhostSize.Width, Height = DesktopDropWindow.GhostSize.Height };
        _layout.Panes.Add(pane);
        LayoutEditor.MoveItems(_layout, drag.Ids, pane.Id);
        var window = ShowPane(pane);
        if (DesktopLayer.GetBounds(window) is { } bounds) DesktopLayer.SetBounds(window, x, y, bounds.Width, bounds.Height);
        window.SnapToGridNow();
        pane.Monitor = MonitorOf(window) ?? pane.Monitor;
        RefreshItems();
        SaveLayout();
        // Na het slepen meteen een naam laten typen.
        Dispatcher.InvokeAsync(window.BeginRename, DispatcherPriority.ApplicationIdle);
    }

    private async Task DeletePaneAsync(PaneWindow window)
    {
        var pane = window.ActivePane; // met tabbladen: het tabblad dat open staat
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
                            Loc.F("Pane \"{0}\" verwijderen? De {1} taken erin worden ook verwijderd.", pane.Title, count),
                            "DonkiePane", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
                        if (answer != MessageBoxResult.Yes) return;
                    }
                }
                await provider.DeleteListAsync(listId);
            }
            catch (Exception ex) when (TaskErrors.IsHandled(ex))
            {
                // Aangeroepen vanuit een async void-handler: een fout hier mag de app niet laten crashen.
                MessageBox.Show(Loc.F("Pane \"{0}\" is niet verwijderd. {1}", pane.Title, TaskErrors.Message(ex)),
                    "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (!LayoutEditor.DeletePane(_layout, pane.Id)) return;
        ForgetContent(pane);
        SyncWindows();
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
            _gridOverlay?.Close(); // hing aan het oude bureaublad
            _gridOverlay = null;
            foreach (var dropWindow in _dropWindows) dropWindow.Close(); // idem
            _dropWindows.Clear();
            StartRecycleBinWatcher(); // de aanmelding bij de oude Verkenner geldt niet meer
            RecreatePaneWindows();
            ApplyDesktopIconsSetting();
        };
        timer.Start();
    }

    /// <summary>Sluit alle pane-vensters en bouwt ze opnieuw op uit de indeling.</summary>
    private void RecreatePaneWindows()
    {
        foreach (var window in _windows.ToList())
        {
            window.ReleaseContent();
            window.Close();
        }
        _windows.Clear();
        _panesHidden = false; // de nieuwe panes zijn zichtbaar
        foreach (var pane in _contents.Keys.ToList()) ForgetContent(pane);
        foreach (var pane in _layout.Panes.Where(p => p.TabHost is null).ToList())
            ShowPane(pane);
        RefreshItems();
        EnsurePanesVisible();
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
            _balloonClicked = null;
            _trayIcon.ShowBalloonTip(10_000, Loc.T("DonkiePane kon je indeling niet opslaan"),
                Loc.F("{0} DonkiePane probeert het bij de volgende wijziging opnieuw.", ex.Message), Forms.ToolTipIcon.Warning);
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
        menu.Items.Add(Loc.T("Nieuw iconenpane"), null, (_, _) => AddPane(new Pane()));
        var newTasks = new Forms.ToolStripMenuItem(Loc.T("Nieuw takenpane"));
        newTasks.DropDownOpening += (_, _) => FillNewTasksMenu(newTasks);
        FillNewTasksMenu(newTasks);
        menu.Items.Add(newTasks);
        menu.Items.Add(Loc.T("Nieuw mappane…"), null, (_, _) => AddFolderPane());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Panes verbergen of tonen"), null, (_, _) => TogglePanesHidden());
        menu.Items.Add(Loc.T("Panes naar voren halen"), null, (_, _) => BeginPeek());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Instellingen…"), null, (_, _) => ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Afsluiten"), null, (_, _) => Shutdown());

        var icon = new Forms.NotifyIcon
        {
            Icon = TrayIconFactory.CreateAppIcon(),
            Text = "DonkiePane",
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => ShowSettings();
        icon.BalloonTipClicked += (_, _) => _balloonClicked?.Invoke();
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
        StopPeek();
        SaveLayout();
        StopUpdateChecks();
        StopDueReminders();
        _dimTimer?.Stop();
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


using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DonkiePane.Core;
using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>Instellingen die voor alle panes gelden: licht of donker, hoe rond de hoeken zijn, en hoe een opgerold pane opengaat.</summary>
public sealed record PaneStyle(bool Light, double CornerRadius, RollUpOpen RollUpOpen)
{
    public static readonly PaneStyle Default = new(false, Layout.DefaultCornerRadius, RollUpOpen.Button);
}

/// <summary>
/// Het venster om een pane heen: titelbalk, verslepen, grootte, oprollen, hernoemen, thema, vastklikken op het raster
/// en de koppeling met het bureaublad. Er kunnen meerdere panes in één venster staan, als tabbladen: <see cref="Pane"/>
/// bepaalt de plek en grootte, <see cref="ActivePane"/> wat je ziet (en waar het menu over gaat). De inhoud (iconen,
/// taken of een map) komt via <see cref="SetTabs"/>.
/// </summary>
public partial class PaneWindow : Window
{
    private const double TitleBarHeight = 36; // kop plus de rand eromheen
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_SIZING = 0x0214;
    private const int WM_MOVING = 0x0216;
    private const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4, WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    /// <summary>Hoe dicht bij de rand (in WPF-eenheden) je het pane kunt vergroten.</summary>
    private const double ResizeBorder = 6;

    private Rect _boundsAtMoveStart;
    private bool _sizing;                       // tijdens vergroten (niet verplaatsen)
    private IReadOnlyList<Pane> _tabs;
    private double _radius = Layout.DefaultCornerRadius;
    private ImageBrush? _backdropBrush;

    // Opgerold pane even open (bij aanwijzen of een klik); het blijft opgerold.
    private bool _peekOpen;
    private int _outsideTicks;
    private readonly DispatcherTimer _peekTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

    // Een tabblad vastgepakt: losmaken als je het uit de titelbalk trekt.
    private Pane? _tabDragPane;
    private Point _tabDragStart;
    private readonly DispatcherTimer _tabDropTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Pane? _tabDropPane;

    /// <summary>Het pane van dit venster: plek, grootte en opgerold zijn van dit pane.</summary>
    public Pane Pane { get; }

    /// <summary>Het tabblad dat je nu ziet (zonder tabbladen: <see cref="Pane"/>).</summary>
    public Pane ActivePane { get; private set; }

    /// <summary>Alle tabbladen in dit venster, in volgorde.</summary>
    public IReadOnlyList<Pane> Tabs => _tabs;

    /// <summary>Of en hoe groot het raster is; wordt tijdens het slepen en bij het loslaten gelezen.</summary>
    public Func<(bool Snap, double Cell)> GridSettings { get; set; } = () => (false, GridSnap.DefaultCell);

    /// <summary>Licht/donker, hoeken en oprollen; gelezen bij <see cref="RefreshLook"/> en bij aanwijzen.</summary>
    public Func<PaneStyle> LookSettings { get; set; } = () => PaneStyle.Default;

    /// <summary>De panes waar dit pane een tabblad van kan worden (voor het menu).</summary>
    public Func<IEnumerable<Pane>> MergeTargets { get; set; } = () => [];

    /// <summary>Positie, grootte, titel, thema of status is veranderd en moet bewaard worden.</summary>
    public event EventHandler? PaneChanged;

    /// <summary><see cref="ActivePane"/> moet verwijderd worden.</summary>
    public event EventHandler? DeleteRequested;

    /// <summary>Weergave of sortering van <see cref="ActivePane"/> is veranderd: de iconen opnieuw tonen.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>Het verplaatsen of vergroten begint/eindigt (om het raster te tonen).</summary>
    public event EventHandler? SizeMoveStarted;
    public event EventHandler? SizeMoveEnded;

    /// <summary>Een ander tabblad is aangeklikt.</summary>
    public event EventHandler<Pane>? TabSelected;

    /// <summary>Een tabblad is uit de titelbalk getrokken (of via het menu losgemaakt): het moet een eigen venster krijgen.</summary>
    public event EventHandler<Pane>? TabDetachRequested;

    /// <summary>Via het menu: <see cref="ActivePane"/> moet een tabblad worden van dit pane.</summary>
    public event EventHandler<Pane>? MergeRequested;

    /// <summary>Een mappane moet een andere map tonen.</summary>
    public event EventHandler? FolderChangeRequested;

    /// <summary>Het venster wordt versleept; de muis staat hier (fysieke schermpixels).</summary>
    public event EventHandler<System.Drawing.Point>? Moving;

    /// <summary>Het venster is versleept en losgelaten; de muis staat hier (fysieke schermpixels).</summary>
    public event EventHandler<System.Drawing.Point>? MoveFinished;

    public PaneWindow(Pane pane)
    {
        InitializeComponent();
        Pane = pane;
        ActivePane = pane;
        _tabs = [pane];
        DesktopLayer.Attach(this);

        TitleText.Text = pane.Title;
        Left = pane.Left;
        Top = pane.Top;
        Width = pane.Width;
        ApplyRolledUp();
        RefreshLook();
        // De inhoud volgt de afgeronde hoeken (anders steken de titelbalk en iconen bij het scrollen uit de hoek).
        Chrome.SizeChanged += (_, _) => ClipChrome();
        TitleGrid.SizeChanged += (_, _) => SizeTabs();
        BuildThemeMenu();
        BuildViewMenus();
        ApplyKindMenus();
        PreviewMouseWheel += OnPreviewMouseWheel;

        LocationChanged += (_, _) =>
        {
            Pane.Left = Left;
            Pane.Top = Top;
            UpdateBackdrop();
        };
        SizeChanged += (_, _) =>
        {
            Pane.Width = ActualWidth;
            if (!Pane.IsRolledUp) Pane.Height = ActualHeight;
            UpdateBackdrop();
        };
        SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
        Loaded += (_, _) => UpdateBackdrop();

        _peekTimer.Tick += (_, _) => CheckPeekOpen();
        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            if (IsMouseOver && LookSettings().RollUpOpen == RollUpOpen.Hover) OpenRolledUpForAWhile();
        };
        MouseEnter += (_, _) =>
        {
            if (Pane.IsRolledUp && !_peekOpen && LookSettings().RollUpOpen == RollUpOpen.Hover) _hoverTimer.Start();
        };
        MouseLeave += (_, _) => _hoverTimer.Stop();
        _tabDropTimer.Tick += (_, _) =>
        {
            _tabDropTimer.Stop();
            if (_tabDropPane is { } pane && pane != ActivePane) TabSelected?.Invoke(this, pane);
        };

        WallpaperBackdrop.Changed += OnWallpaperChanged;
    }

    /// <summary>Staat deze inhoud nu in dit venster?</summary>
    public bool Shows(UIElement content) => ReferenceEquals(Body.Content, content);

    /// <summary>Laat de inhoud los (voor het sluiten), zodat een ander venster hem kan tonen.</summary>
    public void ReleaseContent() => Body.Content = null;

    /// <summary>Zet het venster op de plek en grootte uit de indeling (na samenvoegen of losmaken van tabbladen).</summary>
    public void ApplyPaneBounds()
    {
        if (Left != Pane.Left) Left = Pane.Left;
        if (Top != Pane.Top) Top = Pane.Top;
        if (Width != Pane.Width) Width = Pane.Width;
        _peekOpen = false;
        _peekTimer.Stop();
        ApplyRolledUp();
    }

    /// <summary>Eén pane zonder tabbladen met deze inhoud.</summary>
    public void SetContent(UIElement content) => SetTabs([Pane], Pane, content);

    /// <summary>De tabbladen van dit venster, welk tabblad open staat en de inhoud daarvan.</summary>
    public void SetTabs(IReadOnlyList<Pane> tabs, Pane active, UIElement content)
    {
        if (TitleEditor.IsVisible) EndRename(commit: true);
        var sameTabs = tabs.SequenceEqual(_tabs);
        _tabs = tabs.Count > 0 ? tabs : [Pane];
        ActivePane = active;
        if (!ReferenceEquals(Body.Content, content)) Body.Content = content;
        TitleText.Text = active.Title;
        if (sameTabs && TabStrip.Children.Count == _tabs.Count) UpdateTabs();
        else BuildTabs();
        RefreshLook();
        BuildThemeMenu();
        BuildViewMenus();
        ApplyKindMenus();
    }

    private void ApplyKindMenus()
    {
        DeleteMenuItem.Visibility = ActivePane.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        ChooseFolderMenuItem.Visibility = ActivePane.Kind == PaneKind.Folder ? Visibility.Visible : Visibility.Collapsed;
        DetachMenuItem.Visibility = _tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Thema, kleur, materiaal en transparantie ----------

    /// <summary>Kleuren, licht/donker, hoeken en materiaal opnieuw toepassen (na een wijziging in Instellingen of aan het pane).</summary>
    public void RefreshLook()
    {
        var style = LookSettings();
        PaneThemes.Apply(Resources, ActivePane, style.Light);
        _radius = Math.Clamp(double.IsFinite(style.CornerRadius) ? style.CornerRadius : Layout.DefaultCornerRadius, 0, Layout.MaxCornerRadius);
        Frame.CornerRadius = new CornerRadius(_radius);
        Backdrop.CornerRadius = new CornerRadius(_radius);
        ClipChrome();
        UpdateTabs();
        UpdateBackdrop();
        if (!Pane.IsRolledUp || style.RollUpOpen == RollUpOpen.Button) CloseRolledUpPeek();
    }

    private void ClipChrome()
    {
        if (Chrome.ActualWidth <= 0) return;
        var inner = Math.Max(0, _radius - 1);
        Chrome.Clip = new RectangleGeometry(new Rect(0, 0, Chrome.ActualWidth, Chrome.ActualHeight), inner, inner);
    }

    private void OnWallpaperChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(() =>
    {
        _backdropBrush = null;
        UpdateBackdrop();
    });

    /// <summary>Mica: het stuk vervaagde bureaubladachtergrond dat precies onder dit venster ligt.</summary>
    private void UpdateBackdrop()
    {
        if (ActivePane.Material != PaneMaterial.Mica || IsClosed)
        {
            Backdrop.Visibility = Visibility.Collapsed;
            return;
        }
        if (DesktopLayer.GetBounds(this) is not { } bounds || WallpaperBackdrop.For(bounds) is not { } sample)
        {
            Backdrop.Visibility = Visibility.Collapsed;
            return;
        }
        if (_backdropBrush is null || !ReferenceEquals(_backdropBrush.ImageSource, sample.Image))
        {
            _backdropBrush = new ImageBrush(sample.Image) { Stretch = Stretch.Fill, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox };
            Backdrop.Background = _backdropBrush;
        }
        _backdropBrush.Viewbox = sample.Viewbox;
        Backdrop.Visibility = Visibility.Visible;
    }

    private void BuildThemeMenu()
    {
        // Alleen de themakleuren vernieuwen; de vaste opties eronder (eigen kleur, transparantie, patroon) blijven staan.
        foreach (var old in ThemeMenu.Items.OfType<MenuItem>().Where(i => i.Tag is string).ToList())
            ThemeMenu.Items.Remove(old);
        var current = PaneThemes.Get(ActivePane.Theme).Id;
        var hasCustom = PaneThemes.TryParseColor(ActivePane.CustomColor, out _);
        var index = 0;
        foreach (var theme in PaneThemes.All)
        {
            var item = new MenuItem { Header = Loc.T(theme.Name), IsCheckable = true, IsChecked = !hasCustom && theme.Id == current, Tag = theme.Id };
            item.Click += Theme_Click;
            ThemeMenu.Items.Insert(index++, item);
        }

        TransparencyMenu.Items.Clear();
        var currentPercent = (int)Math.Round((1 - ActivePane.EffectiveOpacity) * 100);
        foreach (var percent in PaneThemes.TransparencySteps)
        {
            var item = new MenuItem
            {
                Header = percent == 0 ? Loc.T("0% (massief)") : $"{percent}%",
                IsCheckable = true,
                IsChecked = Math.Abs(percent - currentPercent) < 5,
                Tag = percent,
            };
            item.Click += Transparency_Click;
            TransparencyMenu.Items.Add(item);
        }

        PatternMenuItem.IsChecked = ActivePane.ShowPattern;
        MicaMenuItem.IsChecked = ActivePane.Material == PaneMaterial.Mica;
    }

    private void LookChanged()
    {
        RefreshLook();
        BuildThemeMenu();
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string id }) return;
        ActivePane.Theme = id;
        ActivePane.CustomColor = null; // een gekozen thema vervangt een eigen kleur
        LookChanged();
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        var start = PaneThemes.TryParseColor(ActivePane.CustomColor, out var c) ? c
            : (Color)ColorConverter.ConvertFromString(PaneThemes.Resolve(ActivePane).Title);
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(start.R, start.G, start.B),
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        ActivePane.CustomColor = PaneThemes.ToHex(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        LookChanged();
    }

    private void Transparency_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int percent }) return;
        ActivePane.Opacity = 1 - percent / 100.0;
        LookChanged();
    }

    private void Pattern_Click(object sender, RoutedEventArgs e)
    {
        ActivePane.ShowPattern = PatternMenuItem.IsChecked;
        LookChanged();
    }

    private void Mica_Click(object sender, RoutedEventArgs e)
    {
        ActivePane.Material = MicaMenuItem.IsChecked ? PaneMaterial.Mica : PaneMaterial.Solid;
        LookChanged();
    }

    private void ResetLook_Click(object sender, RoutedEventArgs e)
    {
        ActivePane.CustomColor = null;
        ActivePane.Opacity = Pane.DefaultOpacity;
        ActivePane.ShowPattern = false;
        ActivePane.Material = PaneMaterial.Solid;
        LookChanged();
    }

    // ---------- Weergave en sortering (iconen- en mappanes) ----------

    private static readonly (IconSize Size, string Name)[] Sizes =
    [
        (IconSize.Large, "Grote iconen"),
        (IconSize.Small, "Kleine iconen"),
        (IconSize.List, "Lijst"),
    ];

    private static readonly (ItemSortOrder Order, string Name)[] SortOrders =
    [
        (ItemSortOrder.Manual, "Eigen volgorde"),
        (ItemSortOrder.Name, "Naam"),
        (ItemSortOrder.Type, "Type"),
        (ItemSortOrder.Modified, "Laatst gewijzigd"),
    ];

    private void BuildViewMenus()
    {
        var pane = ActivePane;
        var icons = pane.Kind is PaneKind.Icons or PaneKind.Folder;
        ViewMenu.Visibility = SortMenu.Visibility = icons ? Visibility.Visible : Visibility.Collapsed;
        if (!icons) return;

        ViewMenu.Items.Clear();
        foreach (var (size, name) in Sizes)
        {
            var item = new MenuItem { Header = Loc.T(name), IsCheckable = true, IsChecked = pane.IconSize == size, Tag = size };
            item.Click += Size_Click;
            ViewMenu.Items.Add(item);
        }

        // Eigen icoongrootte bovenop de weergave, zoals Ctrl+scrollwiel in Verkenner.
        var pixels = IconMetrics.Pixels(pane);
        ViewMenu.Items.Add(new Separator());
        var larger = new MenuItem { Header = Loc.T("Grotere iconen"), InputGestureText = Loc.T("Ctrl+scrollwiel"), IsEnabled = pixels < IconMetrics.Max };
        larger.Click += (_, _) => StepIconSize(1);
        var smaller = new MenuItem { Header = Loc.T("Kleinere iconen"), IsEnabled = pixels > IconMetrics.Min };
        smaller.Click += (_, _) => StepIconSize(-1);
        var reset = new MenuItem { Header = Loc.T("Standaardgrootte"), IsEnabled = pane.IconPixels is not null };
        reset.Click += (_, _) => SetIconPixels(null);
        ViewMenu.Items.Add(larger);
        ViewMenu.Items.Add(smaller);
        ViewMenu.Items.Add(reset);

        // Een lijst heeft de naam naast het icoon nodig; daar is deze keuze er niet.
        if (pane.IconSize != IconSize.List)
        {
            ViewMenu.Items.Add(new Separator());
            var names = new MenuItem { Header = Loc.T("Namen tonen"), IsCheckable = true, IsChecked = pane.ShowNames };
            names.Click += (_, _) =>
            {
                ActivePane.ShowNames = !ActivePane.ShowNames;
                AfterViewChange();
            };
            ViewMenu.Items.Add(names);
        }

        // Een map heeft geen eigen volgorde: daar is "eigen volgorde" gewoon op naam.
        var folder = pane.Kind == PaneKind.Folder;
        var currentOrder = folder && pane.SortBy == ItemSortOrder.Manual ? ItemSortOrder.Name : pane.SortBy;
        SortMenu.Items.Clear();
        foreach (var (order, name) in SortOrders)
        {
            if (folder && order == ItemSortOrder.Manual) continue;
            var item = new MenuItem { Header = Loc.T(name), IsCheckable = true, IsChecked = currentOrder == order, Tag = order };
            item.Click += Sort_Click;
            SortMenu.Items.Add(item);
        }
    }

    private void Size_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: IconSize size }) return;
        ActivePane.IconSize = size;
        ActivePane.IconPixels = null; // een weergave kiezen is ook zijn eigen grootte kiezen
        AfterViewChange();
    }

    /// <summary>Ctrl+scrollwiel boven een iconen- of mappane: iconen groter of kleiner.</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || ActivePane.Kind is not (PaneKind.Icons or PaneKind.Folder)) return;
        e.Handled = true; // niet ook nog scrollen
        StepIconSize(Math.Sign(e.Delta));
    }

    internal void StepIconSize(int steps)
    {
        if (steps == 0) return;
        var next = IconMetrics.Step(IconMetrics.Pixels(ActivePane), steps);
        SetIconPixels(next == IconMetrics.Default(ActivePane.IconSize) ? null : next);
    }

    private void SetIconPixels(int? pixels)
    {
        if (ActivePane.IconPixels == pixels) return;
        ActivePane.IconPixels = pixels;
        AfterViewChange();
    }

    private void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: ItemSortOrder order }) return;
        ActivePane.SortBy = order;
        AfterViewChange();
    }

    private void AfterViewChange()
    {
        BuildViewMenus(); // het vinkje bij de nieuwe keuze (ook als je dezelfde nog eens kiest)
        ViewChanged?.Invoke(this, EventArgs.Empty);
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e) => FolderChangeRequested?.Invoke(this, EventArgs.Empty);

    // ---------- Tabbladen ----------

    private void BuildTabs()
    {
        TabStrip.Children.Clear();
        var multi = _tabs.Count > 1;
        TabStrip.Visibility = multi && !TitleEditor.IsVisible ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Visibility = multi || TitleEditor.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        if (!multi) return;
        foreach (var tab in _tabs) TabStrip.Children.Add(CreateTab(tab));
        UpdateTabs();
        SizeTabs();
    }

    private Border CreateTab(Pane pane)
    {
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "PaneTitleText");
        text.SetResourceReference(TextBlock.FontFamilyProperty, "UiDisplayFont");
        var content = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        content.Children.Add(dot);
        content.Children.Add(text);

        var tab = new Border
        {
            Tag = pane,
            Height = 26,
            Margin = new Thickness(0, 0, 2, 0),
            Padding = new Thickness(8, 0, 8, 0),
            CornerRadius = new CornerRadius(5),
            VerticalAlignment = VerticalAlignment.Center,
            AllowDrop = true,
            Child = content,
            ToolTip = Loc.T("Klik om te openen; sleep uit de titelbalk om los te maken"),
        };
        tab.MouseLeftButtonDown += Tab_MouseLeftButtonDown;
        tab.MouseMove += Tab_MouseMove;
        tab.MouseLeftButtonUp += Tab_MouseLeftButtonUp;
        tab.PreviewMouseRightButtonDown += (_, _) => SelectTab(pane);
        tab.DragEnter += (_, e) =>
        {
            // Bestanden boven een tabblad: even wachten en dan dat tabblad openen, zodat je erin kunt neerzetten.
            _tabDropPane = pane;
            _tabDropTimer.Stop();
            _tabDropTimer.Start();
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        };
        tab.DragLeave += (_, _) => _tabDropTimer.Stop();
        return tab;
    }

    /// <summary>Titels, kleuren en welk tabblad open staat bijwerken, zonder de tabbladen opnieuw te maken.</summary>
    private void UpdateTabs()
    {
        var light = LookSettings().Light;
        foreach (var tab in TabStrip.Children.OfType<Border>())
        {
            if (tab.Tag is not Pane pane || tab.Child is not DockPanel { Children: [System.Windows.Shapes.Ellipse dot, TextBlock text] }) continue;
            var active = ReferenceEquals(pane, ActivePane);
            text.Text = pane.Title;
            text.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            text.Opacity = active ? 1 : 0.78;
            dot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(PaneThemes.Resolve(pane, light).Title));
            dot.Stroke = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
            dot.StrokeThickness = 1;
            tab.Background = active ? new SolidColorBrush(light ? Color.FromArgb(0x24, 0, 0, 0) : Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)) : Brushes.Transparent;
        }
    }

    /// <summary>Tabbladen delen de breedte, met rechts altijd wat ruimte om het venster aan te verslepen.</summary>
    private void SizeTabs()
    {
        var tabs = TabStrip.Children.OfType<Border>().ToList();
        if (tabs.Count == 0) return;
        var available = Math.Max(0, TitleGrid.ActualWidth - HeaderButtons.ActualWidth - 32) - tabs.Count * 2;

        // Zo breed als de titel nodig heeft (hooguit 180). Past het niet, dan krijgen korte titels wat ze nodig hebben en
        // delen de lange de rest.
        foreach (var tab in tabs) tab.Width = double.NaN;
        var wanted = tabs.Select(t => { t.Measure(new Size(double.PositiveInfinity, 26)); return Math.Min(180, Math.Ceiling(t.DesiredSize.Width)); }).ToList();
        var widths = new double[tabs.Count];
        var open = Enumerable.Range(0, tabs.Count).ToList();
        var left = available;
        while (open.Count > 0)
        {
            var share = left / open.Count;
            var fitting = open.Where(i => wanted[i] <= share).ToList();
            if (fitting.Count == 0)
            {
                foreach (var i in open) widths[i] = Math.Max(44, share);
                break;
            }
            foreach (var i in fitting)
            {
                widths[i] = wanted[i];
                left -= wanted[i];
                open.Remove(i);
            }
        }
        for (var i = 0; i < tabs.Count; i++) tabs[i].Width = widths[i];
    }

    private void SelectTab(Pane pane)
    {
        if (!ReferenceEquals(pane, ActivePane)) TabSelected?.Invoke(this, pane);
    }

    private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: Pane pane } tab) return;
        e.Handled = true; // niet het venster verslepen
        SelectTab(pane);
        if (e.ClickCount == 2)
        {
            BeginRename();
            return;
        }
        _tabDragPane = pane;
        _tabDragStart = e.GetPosition(TitleBar);
        tab.CaptureMouse();
    }

    private void Tab_MouseMove(object sender, MouseEventArgs e)
    {
        if (_tabDragPane is not { } pane || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(TitleBar);
        var outside = Math.Abs(p.Y - _tabDragStart.Y) > 14 || p.X < -8 || p.X > TitleBar.ActualWidth + 8;
        if (!outside) return;
        _tabDragPane = null;
        ((UIElement)sender).ReleaseMouseCapture();
        TabDetachRequested?.Invoke(this, pane);
    }

    private void Tab_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _tabDragPane = null;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void Detach_Click(object sender, RoutedEventArgs e) => TabDetachRequested?.Invoke(this, ActivePane);

    private void TitleMenu_Opened(object sender, RoutedEventArgs e)
    {
        // De panes waar dit pane een tabblad van kan worden; vers, want er kunnen er bij gekomen of weg zijn.
        MergeMenu.Items.Clear();
        foreach (var target in MergeTargets())
        {
            var item = new MenuItem { Header = target.Title, Tag = target };
            item.Click += (_, _) => MergeRequested?.Invoke(this, target);
            MergeMenu.Items.Add(item);
        }
        MergeMenu.IsEnabled = MergeMenu.Items.Count > 0;
    }

    /// <summary>Staat dit punt (fysieke schermpixels) op de titelbalk van dit venster?</summary>
    public bool TitleBarContains(System.Drawing.Point screen)
    {
        if (IsClosed || !IsVisible) return false;
        try
        {
            var p = TitleBar.PointFromScreen(new Point(screen.X, screen.Y));
            return p.X >= 0 && p.Y >= 0 && p.X <= TitleBar.ActualWidth && p.Y <= TitleBar.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Laat zien dat een versleept pane hier een tabblad wordt als je het loslaat.</summary>
    public void SetMergeHighlight(bool on) => MergeHighlight.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    private void TitleBar_DragEnter(object sender, DragEventArgs e)
    {
        OpenRolledUpForAWhile(); // bestanden boven een opgerold pane: open, zodat je erin kunt neerzetten
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void TitleBar_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    // ---------- Verplaatsen en vergroten, vastklikken op het raster ----------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_ENTERSIZEMOVE:
                _boundsAtMoveStart = new Rect(Left, Top, ActualWidth, ActualHeight);
                _sizing = false;
                SizeMoveStarted?.Invoke(this, EventArgs.Empty);
                break;
            case WM_EXITSIZEMOVE:
                SnapToGrid();
                SizeMoveEnded?.Invoke(this, EventArgs.Empty);
                if (!_sizing) MoveFinished?.Invoke(this, DesktopLayer.CursorPosition());
                PaneChanged?.Invoke(this, EventArgs.Empty);
                break;
            case WM_MOVING:
                SnapWhileDragging(lParam, null);
                Moving?.Invoke(this, DesktopLayer.CursorPosition());
                break;
            case WM_SIZING:
                _sizing = true;
                SnapWhileDragging(lParam, (int)wParam);
                break;
            case WM_NCHITTEST when ResizeMode != ResizeMode.NoResize:
                var hit = ResizeHitTest(lParam);
                if (hit != 0)
                {
                    handled = true;
                    return hit;
                }
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Vergroten vanaf elke rand en hoek, zoals een gewoon venster (zonder zichtbaar handvat).</summary>
    private IntPtr ResizeHitTest(IntPtr lParam)
    {
        var screen = new Point((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
        Point p;
        try { p = PointFromScreen(screen); }
        catch (InvalidOperationException) { return IntPtr.Zero; } // nog niet getoond
        var w = ActualWidth;
        var h = ActualHeight;
        if (p.X < 0 || p.Y < 0 || p.X > w || p.Y > h) return IntPtr.Zero;

        var left = p.X < ResizeBorder;
        var right = p.X > w - ResizeBorder;
        var top = p.Y < ResizeBorder / 2; // bovenaan zit de titelbalk: daar alleen een smal randje
        var bottom = p.Y > h - ResizeBorder;
        var corner = ResizeBorder * 2;
        var nearLeft = p.X < corner;
        var nearRight = p.X > w - corner;
        var nearBottom = p.Y > h - corner;

        return (left, right, top, bottom) switch
        {
            _ when (bottom && nearRight) || (right && nearBottom) => HTBOTTOMRIGHT,
            _ when (bottom && nearLeft) || (left && nearBottom) => HTBOTTOMLEFT,
            _ when top && nearLeft => HTTOPLEFT,
            _ when top && nearRight => HTTOPRIGHT,
            (true, _, _, _) => HTLEFT,
            (_, true, _, _) => HTRIGHT,
            (_, _, true, _) => HTTOP,
            (_, _, _, true) => HTBOTTOM,
            _ => IntPtr.Zero,
        };
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    /// <summary>
    /// Magnetisch raster: terwijl je sleept, springt het pane al van rasterlijn naar
    /// rasterlijn, zodat je ziet waar het komt. Windows geeft de nieuwe plek in
    /// fysieke pixels; die passen we aan voordat het venster daar echt staat.
    /// <paramref name="sizingEdge"/> is <c>null</c> bij verplaatsen.
    /// </summary>
    private void SnapWhileDragging(IntPtr lParam, int? sizingEdge)
    {
        var (snap, cell) = GridSettings();
        if (!snap || cell <= 0) return;

        var r = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeRect>(lParam);
        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        double left = r.Left / sx, top = r.Top / sy, right = r.Right / sx, bottom = r.Bottom / sy;

        if (sizingEdge is not int edge)
        {
            var width = right - left;
            var height = bottom - top;
            left = GridSnap.Position(left, cell);
            top = GridSnap.Position(top, cell);
            right = left + width;
            bottom = top + height;
        }
        else
        {
            var dragLeft = edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT;
            var dragRight = edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT;
            var dragTop = edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT;
            var dragBottom = edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;
            (left, top, right, bottom) = GridSnap.Resize(left, top, right, bottom,
                dragLeft, dragTop, dragRight, dragBottom, cell, MinWidth, MinHeight, snapHeight: !Pane.IsRolledUp);
        }

        var snapped = new NativeRect
        {
            Left = (int)Math.Round(left * sx),
            Top = (int)Math.Round(top * sy),
            Right = (int)Math.Round(right * sx),
            Bottom = (int)Math.Round(bottom * sy),
        };
        System.Runtime.InteropServices.Marshal.StructureToPtr(snapped, lParam, false);
    }

    /// <summary>Na het plaatsen door de app (bijv. een nieuw pane waar iconen zijn losgelaten): de hoek op het raster.</summary>
    public void SnapToGridNow()
    {
        var (snap, cell) = GridSettings();
        if (!snap) return;
        Left = GridSnap.Position(Left, cell);
        Top = GridSnap.Position(Top, cell);
    }

    /// <summary>
    /// Klikt hoek en afmetingen vast op het raster. Alleen als het pane echt
    /// verplaatst of vergroot is: een klik op de titelbalk laat hem staan.
    /// </summary>
    private void SnapToGrid()
    {
        var (snap, cell) = GridSettings();
        if (!snap) return;

        var moved = Left != _boundsAtMoveStart.X || Top != _boundsAtMoveStart.Y;
        var resized = ActualWidth != _boundsAtMoveStart.Width || ActualHeight != _boundsAtMoveStart.Height;
        if (!moved && !resized) return;

        if (moved)
        {
            Left = GridSnap.Position(Left, cell);
            Top = GridSnap.Position(Top, cell);
        }
        if (resized && !_peekOpen)
        {
            Width = GridSnap.Size(ActualWidth, cell, MinWidth);
            if (!Pane.IsRolledUp) Height = GridSnap.Size(ActualHeight, cell, MinHeight);
        }
    }

    // ---------- Titelbalk ----------

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TitleEditor.IsVisible) return;

        if (e.ClickCount == 2)
        {
            ToggleRolledUp();
            return;
        }

        // DragMove gooit als de knop al los is (komt voor bij aanraken en een pen).
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var before = (Left, Top);
        try
        {
            DragMove(); // blokkeert tot de muis wordt losgelaten
        }
        catch (InvalidOperationException)
        {
            return;
        }

        // Niet verplaatst, dus een klik: bij "met een klik openen" gaat een opgerold pane even open (of weer dicht).
        if (before == (Left, Top) && Pane.IsRolledUp && LookSettings().RollUpOpen == RollUpOpen.Click)
        {
            if (_peekOpen) CloseRolledUpPeek();
            else OpenRolledUpForAWhile();
        }
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gesloten, bijvoorbeeld door Windows bij een herstart van Verkenner (het bureaublad waar het aan hing, is weg).
    /// Een gesloten venster kan niet meer getoond of verborgen worden.
    /// </summary>
    public bool IsClosed { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        IsClosed = true;
        WallpaperBackdrop.Changed -= OnWallpaperChanged;
        _peekTimer.Stop();
        _hoverTimer.Stop();
        _tabDropTimer.Stop();
        base.OnClosed(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // Na vergroten/verkleinen met de grip.
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleRolledUp()
    {
        Pane.IsRolledUp = !Pane.IsRolledUp;
        _peekOpen = false;
        _peekTimer.Stop();
        ApplyRolledUp();
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyRolledUp()
    {
        var open = !Pane.IsRolledUp || _peekOpen;
        Body.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = Body.Visibility;
        ResizeMode = Pane.IsRolledUp ? ResizeMode.NoResize : ResizeMode.CanResize;
        Height = open ? Pane.Height : TitleBarHeight;
        RollButton.Content = Pane.IsRolledUp ? "\uE70D" : "\uE70E"; // pijltje omlaag/omhoog
        RollButton.ToolTip = Pane.IsRolledUp ? Loc.T("Uitrollen") : Loc.T("Oprollen");
    }

    private void Roll_Click(object sender, RoutedEventArgs e) => ToggleRolledUp();

    /// <summary>Is dit opgerolde pane nu even open?</summary>
    public bool IsPeekOpen => _peekOpen;

    /// <summary>Een opgerold pane even openen; het rolt weer op als de muis weggaat. Het blijft opgerold bewaard.</summary>
    public void OpenRolledUpForAWhile()
    {
        if (!Pane.IsRolledUp || _peekOpen || IsClosed) return;
        _peekOpen = true;
        _outsideTicks = 0;
        ApplyRolledUp();
        _peekTimer.Start();
    }

    private void CloseRolledUpPeek()
    {
        _peekTimer.Stop();
        if (!_peekOpen) return;
        _peekOpen = false;
        ApplyRolledUp();
    }

    /// <summary>Elke kwart seconde: is de muis weg (en niet net een menu open of iets aan het slepen), dan weer oprollen.</summary>
    private void CheckPeekOpen()
    {
        if (!_peekOpen) { _peekTimer.Stop(); return; }
        if (StaysOpen()) { _outsideTicks = 0; return; }
        if (++_outsideTicks >= 2) CloseRolledUpPeek();
    }

    private bool StaysOpen()
    {
        if (ShellContextMenu.IsShowing || TitleBar.ContextMenu is { IsOpen: true }) return true;
        if (System.Windows.Forms.Control.MouseButtons != System.Windows.Forms.MouseButtons.None) return true; // slepen of vasthouden
        if (IsKeyboardFocusWithin && Keyboard.FocusedElement is TextBox) return true;                       // naam of filter typen
        if (DesktopLayer.GetBounds(this) is not { } bounds) return false;
        var cursor = DesktopLayer.CursorPosition();
        return cursor.X >= bounds.X && cursor.Y >= bounds.Y && cursor.X < bounds.X + bounds.Width && cursor.Y < bounds.Y + bounds.Height;
    }

    /// <summary>De knop met de drie puntjes opent hetzelfde menu als rechts klikken op de titelbalk.</summary>
    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (TitleBar.ContextMenu is not { } menu) return;
        menu.PlacementTarget = MenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.Closed += ResetPlacement;
        menu.IsOpen = true;

        // Rechts klikken op de titelbalk moet het menu daarna weer bij de muis tonen.
        void ResetPlacement(object? s, RoutedEventArgs a)
        {
            menu.Closed -= ResetPlacement;
            menu.ClearValue(ContextMenu.PlacementTargetProperty);
            menu.ClearValue(ContextMenu.PlacementProperty);
        }
    }

    private void Rename_Click(object sender, RoutedEventArgs e) => BeginRename();

    /// <summary>De titel van het open tabblad bewerken (ook meteen na het maken van een pane door iconen los te laten).</summary>
    public void BeginRename()
    {
        if (IsClosed) return;
        TitleEditor.Text = ActivePane.Title;
        TitleText.Visibility = Visibility.Collapsed;
        TabStrip.Visibility = Visibility.Collapsed;
        TitleEditor.Visibility = Visibility.Visible;
        Activate();
        TitleEditor.Focus();
        TitleEditor.SelectAll();
    }

    private void TitleEditor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EndRename(commit: true);
        else if (e.Key == Key.Escape) EndRename(commit: false);
    }

    private void TitleEditor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        EndRename(commit: true);

    private void EndRename(bool commit)
    {
        if (!TitleEditor.IsVisible) return;
        TitleEditor.Visibility = Visibility.Collapsed;
        var multi = _tabs.Count > 1;
        TitleText.Visibility = multi ? Visibility.Collapsed : Visibility.Visible;
        TabStrip.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;

        var title = TitleEditor.Text.Trim();
        if (!commit || title.Length == 0 || title == ActivePane.Title) return;
        ActivePane.Title = title;
        TitleText.Text = title;
        UpdateTabs();
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Delete_Click(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(this, EventArgs.Empty);
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DonkiePane.Core;
using DonkiePane.Shell;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DonkiePane.App;

/// <summary>
/// Inhoud van een iconenpane. Alles wat je op het bureaublad met een bestand of snelkoppeling doet, kan hier ook:
/// selecteren (klikken, Ctrl/Shift, een kader trekken), openen, slepen naar Verkenner, een app, de Prullenbak, het
/// bureaublad of een ander pane, bestanden hierheen slepen, Delete, F2, Ctrl+C/X/V, Ctrl+Shift+N, Alt+Enter en het
/// menu van Verkenner. De shell van Windows voert het uit, dus meldingen en Prullenbak zijn precies als altijd.
/// </summary>
public partial class IconsView : UserControl
{
    private const string InternalDataFormat = "DonkiePane.Item";

    /// <summary>Wordt er nu iconen uit een pane gesleept (er kan er maar één tegelijk zijn)?</summary>
    internal static ItemDrag? ActiveDrag { get; private set; }

    internal sealed record ItemDrag(IconsView Source, IReadOnlyList<string> Ids);

    /// <summary>Een pane kreeg een selectie: de andere laten de hunne los, zoals op één bureaublad.</summary>
    private static event EventHandler? SelectionClaimed;

    public static readonly DependencyProperty IsCutProperty =
        DependencyProperty.RegisterAttached("IsCut", typeof(bool), typeof(IconsView), new PropertyMetadata(false));

    public static bool GetIsCut(DependencyObject element) => (bool)element.GetValue(IsCutProperty);
    public static void SetIsCut(DependencyObject element, bool value) => element.SetValue(IsCutProperty, value);

    /// <summary>De weergave van het pane; erft door naar elk icoon, zodat de tegel weet hoe groot hij is.</summary>
    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.RegisterAttached("Mode", typeof(IconSize), typeof(IconsView),
            new FrameworkPropertyMetadata(IconSize.Large, FrameworkPropertyMetadataOptions.Inherits));

    public static IconSize GetMode(DependencyObject element) => (IconSize)element.GetValue(ModeProperty);
    public static void SetMode(DependencyObject element, IconSize value) => element.SetValue(ModeProperty, value);

    /// <summary>Hoe groot de iconen zijn (WPF-eenheden); erft door naar elk icoon.</summary>
    public static readonly DependencyProperty IconPixelsProperty =
        DependencyProperty.RegisterAttached("IconPixels", typeof(double), typeof(IconsView),
            new FrameworkPropertyMetadata((double)IconMetrics.Default(IconSize.Large), FrameworkPropertyMetadataOptions.Inherits));

    public static double GetIconPixels(DependencyObject element) => (double)element.GetValue(IconPixelsProperty);
    public static void SetIconPixels(DependencyObject element, double value) => element.SetValue(IconPixelsProperty, value);

    /// <summary>Hoe breed een tegel is (<see cref="double.NaN"/> = zo breed als nodig, bij een lijst); erft door.</summary>
    public static readonly DependencyProperty TileWidthProperty =
        DependencyProperty.RegisterAttached("TileWidth", typeof(double), typeof(IconsView),
            new FrameworkPropertyMetadata(IconMetrics.TileWidth(IconSize.Large, IconMetrics.Default(IconSize.Large), true),
                FrameworkPropertyMetadataOptions.Inherits));

    public static double GetTileWidth(DependencyObject element) => (double)element.GetValue(TileWidthProperty);
    public static void SetTileWidth(DependencyObject element, double value) => element.SetValue(TileWidthProperty, value);

    /// <summary>Namen onder de iconen; erft door naar elk icoon.</summary>
    public static readonly DependencyProperty ShowNamesProperty =
        DependencyProperty.RegisterAttached("ShowNames", typeof(bool), typeof(IconsView),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetShowNames(DependencyObject element) => (bool)element.GetValue(ShowNamesProperty);
    public static void SetShowNames(DependencyObject element, bool value) => element.SetValue(ShowNamesProperty, value);

    /// <summary>Dit icoon is lang niet geopend en wordt grijs getoond.</summary>
    public static readonly DependencyProperty IsDimmedProperty =
        DependencyProperty.RegisterAttached("IsDimmed", typeof(bool), typeof(IconsView), new PropertyMetadata(false));

    public static bool GetIsDimmed(DependencyObject element) => (bool)element.GetValue(IsDimmedProperty);
    public static void SetIsDimmed(DependencyObject element, bool value) => element.SetValue(IsDimmedProperty, value);

    private static readonly HashSet<string> CutIds = new(StringComparer.OrdinalIgnoreCase);
    private static ComIDataObject? _cutData;
    private static DispatcherTimer? _cutTimer;
    private static event EventHandler? CutChanged;

    private IReadOnlyList<ItemView> _allItems = []; // alles in dit pane, in de volgorde op het scherm
    private IReadOnlyList<ItemView> _items = [];    // wat er nu te zien is (na het filter)
    private IconSize _mode = IconSize.Large;
    private HashSet<string> _dimmedIds = new(StringComparer.OrdinalIgnoreCase); // lang niet geopend: grijs
    private bool _sorted;                           // gesorteerd op naam/type/datum: zelf ordenen kan dan niet
    private Point? _dragStart;              // waar de linkerknop op een icoon omlaag ging
    private ListBoxItem? _deferredSelect;    // klik op een al geselecteerd icoon: pas bij loslaten alleen dat selecteren
    private Point? _marqueeStart;            // kader trekken vanaf een lege plek
    private HashSet<ItemView> _marqueeBase = [];
    private ItemView? _renaming;
    private string? _renameWhenArrives;      // net gemaakte map (Ctrl+Shift+N): naam wijzigen zodra hij er is
    private DateTime _renameNextNewUntil;    // via Nieuw gemaakt: het eerstvolgende nieuwe item
    private IReadOnlyList<ItemView>? _pendingItems; // binnengekomen tijdens naam wijzigen; daarna pas tonen

    // Toestand tijdens slepen boven dit pane.
    private bool _dragInside;
    private ComIDataObject? _dragData;
    private ShellDropTarget? _folderTarget;   // de bureaubladmap, voor bestanden van buiten
    private ShellDropTarget? _itemTarget;     // een map, de Prullenbak of een programma in dit pane
    private ListBoxItem? _itemTargetContainer;
    private readonly HashSet<string> _noDropTarget = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Iconen uit een pane zijn hier neergezet (item-id's in volgorde, plek; -1 = achteraan).</summary>
    public event EventHandler<(IReadOnlyList<string> ItemIds, int Index)>? ItemsDropped;

    /// <summary>Er begint/eindigt het slepen van iconen uit dit pane.</summary>
    public event EventHandler? DragStarted;
    public event EventHandler? DragFinished;

    /// <summary>
    /// Er komen zo nieuwe bestanden op het bureaublad die in dít pane horen: geplakt, hierheen gesleept of via
    /// Nieuw gemaakt.
    /// </summary>
    public event EventHandler? ArrivalExpected;

    /// <summary>Je eigen bureaublad.</summary>
    public static string DesktopFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>De map waar geplakte, nieuwe en hierheen gesleepte bestanden heen gaan: het bureaublad, of de map van een mappane.</summary>
    public string TargetFolder { get; set; } = DesktopFolder;

    /// <summary>
    /// Een mappane: de iconen zijn bestanden in <see cref="TargetFolder"/>, niet bureaubladitems. Slepen naar een ander
    /// pane verplaatst dan het bestand (via de shell) in plaats van het icoon in de indeling, en een map openen gaat
    /// erin kijken in plaats van Verkenner te openen.
    /// </summary>
    public bool IsFolderView { get; set; }

    /// <summary>In een mappane is een map geopend (dubbelklik of Enter): erin kijken.</summary>
    public event EventHandler<string>? FolderOpened;

    /// <summary>Een icoon is geopend (dubbelklik, Enter of het menu): telt als gebruikt.</summary>
    public event EventHandler<ItemView>? ItemOpened;

    /// <summary>De filtertekst is veranderd (leeg = filter weg).</summary>
    public event EventHandler<string>? FilterChanged;

    /// <summary>Een lege-plek-tekst in plaats van "Sleep hier iconen naartoe" (bijv. "Deze map is leeg").</summary>
    public void SetEmptyHint(string text) => EmptyHintText.Text = text;

    /// <summary>
    /// Worden iconen uit een pane met het bureaublad hier opnieuw ingedeeld (in plaats van als bestand verplaatst)? Alleen
    /// tussen panes met bureaubladitems.
    /// </summary>
    private bool IsLayoutDrag(DragEventArgs e) =>
        !IsFolderView && (ActiveDrag is { } drag ? !drag.Source.IsFolderView : e.Data.GetDataPresent(InternalDataFormat));

    /// <summary>Iconen uit deze mappane boven deze mappane: neerzetten in dezelfde map doet niets.</summary>
    private bool IsOwnFolderDrag => IsFolderView && ActiveDrag?.Source == this;

    public IconsView()
    {
        InitializeComponent();
        SelectionClaimed += OnSelectionClaimed;
        CutChanged += OnCutChanged;
        Unloaded += (_, _) =>
        {
            SelectionClaimed -= OnSelectionClaimed;
            CutChanged -= OnCutChanged;
        };
        Loaded += (_, _) =>
        {
            SelectionClaimed -= OnSelectionClaimed;
            SelectionClaimed += OnSelectionClaimed;
            CutChanged -= OnCutChanged;
            CutChanged += OnCutChanged;
        };
    }

    public void SetItems(IReadOnlyList<ItemView> items)
    {
        // Tijdens naam wijzigen de lijst niet vervangen: het naamvak zou verdwijnen en de halve naam opslaan.
        if (_renaming is not null)
        {
            _pendingItems = items;
            return;
        }

        var previous = new HashSet<string>(_allItems.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
        _allItems = items;
        ShowItems();

        // Net gemaakt (nieuwe map, Nieuw → ...): meteen de naam laten typen, zoals op het bureaublad.
        var arrived = items.Where(i => !previous.Contains(i.Id)).ToList();
        ItemView? rename = null;
        if (_renameWhenArrives is { } path && items.FirstOrDefault(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)) is { } made)
            rename = made;
        else if (DateTime.UtcNow < _renameNextNewUntil && arrived.Count == 1)
            rename = arrived[0];
        if (rename is not null)
        {
            _renameWhenArrives = null;
            _renameNextNewUntil = default;
            Dispatcher.InvokeAsync(() => BeginRename(rename), DispatcherPriority.Loaded);
        }
    }

    /// <summary>Toont wat er na het filter overblijft; selectie en focus blijven waar ze waren.</summary>
    private void ShowItems()
    {
        var selected = SelectedIds();
        var focusedId = (Keyboard.FocusedElement as ListBoxItem)?.DataContext is ItemView f ? f.Id : null;
        var filter = FilterBar.Visibility == Visibility.Visible ? FilterBox.Text.Trim() : "";
        _items = filter.Length == 0
            ? _allItems
            : _allItems.Where(i => i.DisplayName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                                   (IsFolderView && Path.GetFileName(i.Path).Contains(filter, StringComparison.CurrentCultureIgnoreCase))).ToList();
        ItemsHost.ItemsSource = _items;
        EmptyHint.Visibility = _allItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMatchHint.Visibility = _allItems.Count > 0 && _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (selected.Count > 0) Select(selected);
        if (focusedId is not null && _items.FirstOrDefault(i => i.Id == focusedId) is { } focused)
            Dispatcher.InvokeAsync(() => ContainerOf(focused)?.Focus(), DispatcherPriority.Loaded);
        Dispatcher.InvokeAsync(ApplyItemStates, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Grote of kleine iconen, of een lijst; en of de volgorde gesorteerd is (dan kun je niet zelf ordenen). Met een
    /// eigen icoongrootte (<c>null</c> = die van de weergave) en of de namen onder de iconen staan (een lijst toont
    /// ze altijd).
    /// </summary>
    public void SetView(IconSize mode, bool sorted, int? iconPixels = null, bool showNames = true)
    {
        _sorted = sorted;
        var pixels = IconMetrics.Pixels(mode, iconPixels);
        showNames |= mode == IconSize.List;
        SetIconPixels(ItemsHost, pixels);
        SetTileWidth(ItemsHost, IconMetrics.TileWidth(mode, pixels, showNames));
        SetShowNames(ItemsHost, showNames);
        if (_mode == mode) return;
        _mode = mode;
        SetMode(ItemsHost, mode);
        ItemsHost.ItemsPanel = (ItemsPanelTemplate)FindResource(mode == IconSize.List ? "ListItems" : "WrapItems");
    }

    /// <summary>Deze iconen zijn lang niet geopend en worden grijs; de rest gewoon.</summary>
    public void SetDimmed(IEnumerable<string> ids)
    {
        var dimmed = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        if (dimmed.SetEquals(_dimmedIds)) return;
        _dimmedIds = dimmed;
        ApplyItemStates();
    }

    /// <summary>Welke iconen nu grijs zijn (voor de tests).</summary>
    internal IReadOnlyCollection<string> DimmedIds => _dimmedIds;

    /// <summary>Selecteert precies deze items (onbekende id's tellen niet).</summary>
    public void Select(IEnumerable<string> ids)
    {
        var wanted = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        ItemsHost.SelectedItems.Clear();
        foreach (var item in _items.Where(i => wanted.Contains(i.Id))) ItemsHost.SelectedItems.Add(item);
    }

    private HashSet<string> SelectedIds() =>
        new(ItemsHost.SelectedItems.OfType<ItemView>().Select(i => i.Id), StringComparer.OrdinalIgnoreCase);

    /// <summary>De geselecteerde items in de volgorde van het pane.</summary>
    private List<ItemView> SelectedInOrder()
    {
        var selected = SelectedIds();
        return _items.Where(i => selected.Contains(i.Id)).ToList();
    }

    private static List<string> PathsOf(IEnumerable<ItemView> items) => items.Select(i => i.Path).ToList();

    private ListBoxItem? ContainerOf(ItemView item) => ItemsHost.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;

    private ListBoxItem? ContainerAt(Point pointInItemsHost)
    {
        var hit = ItemsHost.InputHitTest(pointInItemsHost) as DependencyObject;
        while (hit is not null and not ListBoxItem)
            hit = hit is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(hit) : LogicalTreeHelper.GetParent(hit);
        return hit as ListBoxItem;
    }

    private static T? Part<T>(ListBoxItem container, string name) where T : class =>
        container.Template?.FindName(name, container) as T;

    private void OnSelectionClaimed(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(sender, this) && ItemsHost.SelectedItems.Count > 0) ItemsHost.UnselectAll();
    }

    private void ItemsHost_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ItemsHost.SelectedItems.Count > 0 && ItemsHost.IsKeyboardFocusWithin) SelectionClaimed?.Invoke(this, EventArgs.Empty);
    }

    // ---------- Muis: selecteren, openen, kader, slepen ----------

    private void ItemsHost_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            (FindParent<ScrollBar>(source) is not null || (_renaming is not null && FindParent<TextBox>(source) is not null)))
            return; // de scrollbalk, of het naamvak zelf

        var position = e.GetPosition(ItemsHost);
        var container = ContainerAt(position);
        var modifiers = Keyboard.Modifiers;

        if (container is null)
        {
            // Lege plek: selectie weg (behalve met Ctrl) en een kader beginnen.
            if (!modifiers.HasFlag(ModifierKeys.Control)) ItemsHost.UnselectAll();
            _marqueeBase = ItemsHost.SelectedItems.OfType<ItemView>().ToHashSet();
            _marqueeStart = e.GetPosition(Overlay);
            Root.CaptureMouse();
            ItemsHost.Focus();
            e.Handled = true;
            return;
        }

        _dragStart = position;
        if (e.ClickCount == 2 && container.DataContext is ItemView item)
        {
            Open(item);
            _dragStart = null;
            e.Handled = true;
            return;
        }

        // Klik op een al geselecteerd icoon zonder Ctrl/Shift: de selectie blijft staan, zodat je ze samen kunt slepen.
        if (container.IsSelected && modifiers == ModifierKeys.None)
        {
            _deferredSelect = container;
            container.Focus();
            e.Handled = true;
        }
    }

    private void Root_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_marqueeStart is { } start)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { EndMarquee(); return; }
            UpdateMarquee(start, e.GetPosition(Overlay));
            return;
        }

        if (_dragStart is not { } dragStart || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(ItemsHost) - dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStart = null;
        _deferredSelect = null;
        var selected = SelectedInOrder();
        if (selected.Count > 0) StartDrag(selected);
    }

    private void Root_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_marqueeStart is not null)
        {
            EndMarquee();
            e.Handled = true;
        }
        if (_deferredSelect is { DataContext: ItemView item })
        {
            ItemsHost.SelectedItems.Clear();
            ItemsHost.SelectedItems.Add(item);
            ItemsHost.SelectedItem = item;
        }
        _deferredSelect = null;
        _dragStart = null;
    }

    private void Root_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_marqueeStart is not null && !Root.IsMouseCaptured) EndMarquee();
    }

    private void UpdateMarquee(Point start, Point current)
    {
        var rect = new Rect(start, current);
        Canvas.SetLeft(Marquee, rect.X);
        Canvas.SetTop(Marquee, rect.Y);
        Marquee.Width = rect.Width;
        Marquee.Height = rect.Height;
        Marquee.Visibility = Visibility.Visible;

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        foreach (var item in _items)
        {
            if (ContainerOf(item) is not { } container) continue;
            var bounds = container.TransformToVisual(Overlay).TransformBounds(new Rect(container.RenderSize));
            var inside = rect.IntersectsWith(bounds);
            var select = ctrl ? inside ^ _marqueeBase.Contains(item) : inside || _marqueeBase.Contains(item);
            if (container.IsSelected != select) container.IsSelected = select;
        }
    }

    private void EndMarquee()
    {
        _marqueeStart = null;
        Marquee.Visibility = Visibility.Collapsed;
        if (Root.IsMouseCaptured) Root.ReleaseMouseCapture();
        if (ItemsHost.SelectedItems.Count > 0) SelectionClaimed?.Invoke(this, EventArgs.Empty);
    }

    private void ItemsHost_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Rechts klikken op een niet-geselecteerd icoon selecteert alleen dat; op een geselecteerd blijft de selectie.
        if (ContainerAt(e.GetPosition(ItemsHost)) is { } container)
        {
            if (!container.IsSelected)
            {
                ItemsHost.SelectedItems.Clear();
                container.IsSelected = true;
            }
            container.Focus();
        }
        else
        {
            ItemsHost.UnselectAll();
            ItemsHost.Focus();
        }
        e.Handled = true;
    }

    /// <summary>Het menu van Verkenner: voor de selectie, of (op een lege plek) Nieuw en Plakken.</summary>
    private void ItemsHost_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var selected = SelectedInOrder();
        if (selected.Count == 0)
        {
            ShellContextMenu.ShowBackground(TargetFolder,
                beforeCommand: () => ArrivalExpected?.Invoke(this, EventArgs.Empty),
                newItem: () => _renameNextNewUntil = DateTime.UtcNow.AddSeconds(10));
            return;
        }
        ShowItemMenu(selected);
    }

    private void ShowItemMenu(List<ItemView> selected)
    {
        if (ShellContextMenu.TryShow(PathsOf(selected), rename: () => BeginRename(selected[0]),
                invoked: verb => OnShellVerbInvoked(selected, verb)))
            return;

        var menu = (ContextMenu)FindResource("FallbackMenu");
        menu.DataContext = selected[0];
        menu.PlacementTarget = ContainerOf(selected[0]) ?? (UIElement)ItemsHost;
        menu.IsOpen = true;
    }

    private static T? FindParent<T>(DependencyObject element) where T : DependencyObject
    {
        for (DependencyObject? e = element; e is not null; e = e is Visual ? VisualTreeHelper.GetParent(e) : LogicalTreeHelper.GetParent(e))
            if (e is T match) return match;
        return null;
    }

    // ---------- Toetsenbord ----------

    private void ItemsHost_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return; // naam aan het wijzigen
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        var alt = modifiers.HasFlag(ModifierKeys.Alt);
        var selected = SelectedInOrder();

        switch (key)
        {
            case Key.Enter when alt && selected.Count > 0:
                ShellContextMenu.InvokeVerb(PathsOf(selected), "properties");
                break;
            case Key.Enter when selected.Count > 0:
                OpenSelection(selected);
                break;
            case Key.Delete when selected.Count > 0:
                ShellContextMenu.InvokeVerb(PathsOf(selected), "delete", shift);
                break;
            case Key.F2 when selected.Count > 0:
                BeginRename(ItemsHost.SelectedItem as ItemView ?? selected[0]);
                break;
            case Key.C when ctrl && selected.Count > 0:
                SetCut([], null);
                ShellItems.CopyToClipboard(PathsOf(selected), cut: false);
                break;
            case Key.X when ctrl && selected.Count > 0:
                if (ShellItems.CopyToClipboard(PathsOf(selected), cut: true) is { } data) SetCut(selected.Select(i => i.Id), data);
                break;
            case Key.V when ctrl:
                if (!ShellItems.CanPaste()) break;
                ArrivalExpected?.Invoke(this, EventArgs.Empty);
                ShellItems.Paste(TargetFolder);
                break;
            case Key.N when ctrl && shift:
                ArrivalExpected?.Invoke(this, EventArgs.Empty);
                _renameWhenArrives = ShellItems.CreateNewFolder(TargetFolder);
                break;
            case Key.A when ctrl:
                ItemsHost.SelectAll();
                break;
            case Key.F when ctrl:
                OpenFilter("");
                break;
            case Key.Escape when FilterBar.Visibility == Visibility.Visible:
                CloseFilter();
                break;
            case Key.Escape:
                ItemsHost.UnselectAll();
                break;
            case Key.F10 when shift && selected.Count > 0:
            case Key.Apps when selected.Count > 0:
                ShowItemMenu(selected);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ---------- Snel filteren ----------

    /// <summary>Typen in het pane filtert de iconen op naam (in plaats van naar het eerste icoon met die letter te springen).</summary>
    private void ItemsHost_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.OriginalSource is TextBox || _renaming is not null) return;
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if (FilterBar.Visibility != Visibility.Visible && string.IsNullOrWhiteSpace(e.Text)) return; // een spatie begint geen filter
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
        OpenFilter(FilterBar.Visibility == Visibility.Visible ? FilterBox.Text + e.Text : e.Text);
        e.Handled = true;
    }

    /// <summary>Toont het filtervak met deze tekst en zet de cursor erin.</summary>
    internal void OpenFilter(string text)
    {
        FilterBar.Visibility = Visibility.Visible;
        FilterBox.Text = text;
        FilterBox.CaretIndex = text.Length;
        ShowItems();
        Dispatcher.InvokeAsync(() =>
        {
            Window.GetWindow(this)?.Activate();
            FilterBox.Focus();
        }, DispatcherPriority.Input);
    }

    /// <summary>Filter weg: alle iconen weer zichtbaar.</summary>
    internal void CloseFilter(bool focusIcons = true)
    {
        if (FilterBar.Visibility != Visibility.Visible) return;
        FilterBar.Visibility = Visibility.Collapsed;
        FilterBox.Text = "";
        ShowItems();
        FilterChanged?.Invoke(this, "");
        if (focusIcons) ItemsHost.Focus();
    }

    /// <summary>Wat er nu zichtbaar is (voor tests).</summary>
    internal IReadOnlyList<ItemView> VisibleItems => _items;

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (FilterBar.Visibility != Visibility.Visible) return;
        ShowItems();
        FilterChanged?.Invoke(this, FilterBox.Text.Trim());
    }

    /// <summary>De tekst in het filtervak (leeg als het filter dicht is).</summary>
    internal string FilterText => FilterBar.Visibility == Visibility.Visible ? FilterBox.Text.Trim() : "";

    private void FilterBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseFilter();
                break;
            case Key.Enter when _items.Count > 0:
                // Het gekozen icoon, of anders het eerste dat overblijft.
                Open(ItemsHost.SelectedItem as ItemView ?? _items[0]);
                CloseFilter();
                break;
            case Key.Down when _items.Count > 0:
            case Key.Tab when _items.Count > 0:
                ItemsHost.SelectedItems.Clear();
                ItemsHost.SelectedItem = _items[0];
                ContainerOf(_items[0])?.Focus();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Leeg filtervak en ergens anders heen geklikt: het vak mag weg.</summary>
    private void FilterBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FilterBox.Text.Length == 0) CloseFilter(focusIcons: false);
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e) => CloseFilter();

    // ---------- Knippen: half doorzichtig tot het geplakt of vervangen is ----------

    private static void SetCut(IEnumerable<string> ids, ComIDataObject? data)
    {
        CutIds.Clear();
        foreach (var id in ids) CutIds.Add(id);
        _cutData = data;
        if (CutIds.Count > 0)
        {
            _cutTimer ??= CreateCutTimer();
            _cutTimer.Start();
        }
        else _cutTimer?.Stop();
        CutChanged?.Invoke(null, EventArgs.Empty);
    }

    private static DispatcherTimer CreateCutTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            if (_cutData is null || !ShellItems.IsOnClipboard(_cutData)) SetCut([], null);
        };
        return timer;
    }

    private void OnCutChanged(object? sender, EventArgs e) => ApplyItemStates();

    private void ApplyItemStates()
    {
        foreach (var item in _items)
        {
            if (ContainerOf(item) is not { } container) continue;
            SetIsCut(container, CutIds.Contains(item.Id));
            SetIsDimmed(container, _dimmedIds.Contains(item.Id));
        }
    }

    // ---------- Naam wijzigen (F2) ----------

    private void BeginRename(ItemView item)
    {
        if (ShellItems.GetEditingName(item.Path) is not { } name) return;
        if (ContainerOf(item) is not { } container ||
            Part<TextBox>(container, "NameEditor") is not { } editor || Part<TextBlock>(container, "NameText") is not { } text)
            return;

        _renaming = item;
        ItemsHost.SelectedItems.Clear();
        container.IsSelected = true;
        editor.Text = name;
        // Ook als de namen verborgen zijn: tijdens het typen staat het naamvak onder het icoon.
        Part<FrameworkElement>(container, "NameArea")?.SetValue(VisibilityProperty, Visibility.Visible);
        text.Visibility = Visibility.Hidden;
        editor.Visibility = Visibility.Visible;
        editor.KeyDown += Editor_KeyDown;
        editor.LostKeyboardFocus += Editor_LostKeyboardFocus;

        // Zoals Verkenner: alleen de naam selecteren, niet de extensie.
        Dispatcher.InvokeAsync(() =>
        {
            Window.GetWindow(this)?.Activate();
            editor.Focus();
            var dot = File.Exists(item.Path) ? name.LastIndexOf('.') : -1;
            if (dot > 0) editor.Select(0, dot);
            else editor.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { EndRename((TextBox)sender, commit: true); e.Handled = true; }
        else if (e.Key == Key.Escape) { EndRename((TextBox)sender, commit: false); e.Handled = true; }
    }

    private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => EndRename((TextBox)sender, commit: true);

    private void EndRename(TextBox editor, bool commit)
    {
        if (_renaming is not { } item) return;
        _renaming = null;
        editor.KeyDown -= Editor_KeyDown;
        editor.LostKeyboardFocus -= Editor_LostKeyboardFocus;
        editor.Visibility = Visibility.Collapsed;
        if (ContainerOf(item) is { } container)
        {
            if (Part<TextBlock>(container, "NameText") is { } text) text.Visibility = Visibility.Visible;
            Part<FrameworkElement>(container, "NameArea")?.ClearValue(VisibilityProperty);
            container.Focus();
        }

        var name = editor.Text.Trim();
        if (commit && name.Length > 0 && name != ShellItems.GetEditingName(item.Path))
            ShellItems.Rename(item.Path, name); // de watcher van het bureaublad werkt het pane bij

        if (_pendingItems is { } pending)
        {
            _pendingItems = null;
            SetItems(pending);
        }
    }

    // ---------- Slepen vanuit dit pane ----------

    private void StartDrag(List<ItemView> items)
    {
        var ids = items.Select(i => i.Id).ToList();
        if (ItemsHost.IsMouseCaptured) ItemsHost.ReleaseMouseCapture();
        Mouse.Capture(null);

        ActiveDrag = new ItemDrag(this, ids);
        DragStarted?.Invoke(this, EventArgs.Empty);
        try
        {
            // Een echt bestand (zoals Verkenner het sleept): elk doel in Windows begrijpt het. Lukt dat niet, dan
            // alleen tussen panes.
            if (ShellItems.CreateDataObject(PathsOf(items)) is { } data)
                ShellItems.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            else
                DragDrop.DoDragDrop(ItemsHost, new DataObject(InternalDataFormat, string.Join("|", ids)), DragDropEffects.Move);
        }
        finally
        {
            ActiveDrag = null;
            ResetDrop(); // ook als het pane waar het slepen begon geen "weg" meer kreeg
            DragFinished?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---------- Neerzetten op dit pane ----------

    private IntPtr WindowHandle => (PresentationSource.FromVisual(this) as HwndSource)?.Handle ?? IntPtr.Zero;

    private void Root_DragEnter(object sender, DragEventArgs e)
    {
        if (!_dragInside)
        {
            _dragInside = true;
            _dragData = e.Data as ComIDataObject;
            _noDropTarget.Clear();
            UpdateDrop(e);
            DragImages.Enter(WindowHandle, _dragData, e.Effects);
        }
        else UpdateDrop(e);
        e.Handled = true;
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        UpdateDrop(e);
        DragImages.Over(e.Effects);
        e.Handled = true;
    }

    private void Root_DragLeave(object sender, DragEventArgs e)
    {
        // WPF meldt ook "weg" als de muis van het ene onderdeel naar het andere gaat; alleen echt het pane uit telt.
        // De plek uit het event is de laatst bekende plek binnen het pane: bij snel wegslepen ligt die nog binnen, en
        // dan bleef de invoegstreep staan. Daarom de echte plek van de muis.
        if (IsCursorInside()) return;
        ResetDrop();
        DragImages.Leave();
        e.Handled = true;
    }

    private bool IsCursorInside()
    {
        if (!GetCursorPos(out var pt) || PresentationSource.FromVisual(Root) is null) return false;
        var p = Root.PointFromScreen(new Point(pt.X, pt.Y));
        return p.X >= 0 && p.Y >= 0 && p.X < Root.ActualWidth && p.Y < Root.ActualHeight;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint pt);

    private void Root_Drop(object sender, DragEventArgs e)
    {
        UpdateDrop(e);
        var effect = e.Effects;
        var data = _dragData ?? e.Data as ComIDataObject;

        if (_itemTarget is not null && data is not null)
        {
            // Op een map, de Prullenbak of een programma: dat doet de shell (verplaatsen, verwijderen, openen met).
            var targetItem = _itemTargetContainer?.DataContext as ItemView;
            effect = _itemTarget.Drop(data, e.KeyStates, e.AllowedEffects);
            if (effect != DragDropEffects.None && targetItem is not null) ItemOpened?.Invoke(this, targetItem); // het doel is gebruikt
        }
        else if (IsOwnFolderDrag)
        {
            effect = DragDropEffects.None; // naar dezelfde map
        }
        else if (IsLayoutDrag(e) && ActiveDrag is { } drag)
        {
            ItemsDropped?.Invoke(this, (drag.Ids, DropIndex(e)));
            effect = DragDropEffects.Move;
        }
        else if (IsLayoutDrag(e) && e.Data.GetData(InternalDataFormat) is string ids)
        {
            ItemsDropped?.Invoke(this, (ids.Split('|'), DropIndex(e)));
            effect = DragDropEffects.Move;
        }
        else if (_folderTarget is not null && data is not null)
        {
            // Bestanden van buiten (of uit een mappane): naar de map van dit pane, en in dit pane.
            if (!IsFolderView) ArrivalExpected?.Invoke(this, EventArgs.Empty);
            effect = _folderTarget.Drop(data, e.KeyStates, e.AllowedEffects);
        }

        e.Effects = effect;
        DragImages.Drop(data, effect);
        ResetDrop();
        e.Handled = true;
    }

    /// <summary>Bepaalt wat neerzetten hier zou doen, en laat dat zien (markering, invoegstreep, cursor).</summary>
    private void UpdateDrop(DragEventArgs e)
    {
        var internalDrag = IsLayoutDrag(e);
        var container = ContainerAt(e.GetPosition(ItemsHost));

        // 1. Boven het icoon van een map, de Prullenbak of een programma?
        if (container is { DataContext: ItemView item } && IsOverIcon(container, e) &&
            !(ActiveDrag?.Ids.Contains(item.Id, StringComparer.OrdinalIgnoreCase) ?? false) &&
            ItemTargetFor(container, item, e) is { } effect && effect != DragDropEffects.None)
        {
            LeaveFolderTarget();
            InsertMarker.Visibility = Visibility.Collapsed;
            e.Effects = effect;
            return;
        }
        LeaveItemTarget();

        // 2. Iconen uit een pane: hier (opnieuw) neerzetten, op de plek van de streep.
        if (internalDrag)
        {
            LeaveFolderTarget();
            ShowInsertMarker(e);
            e.Effects = DragDropEffects.Move;
            return;
        }

        // 3. Bestanden van buiten: wat de map ermee zou doen (verplaatsen, kopiëren, snelkoppeling).
        InsertMarker.Visibility = Visibility.Collapsed;
        if (IsOwnFolderDrag)
        {
            LeaveFolderTarget();
            e.Effects = DragDropEffects.None;
            return;
        }
        e.Effects = FolderEffect(e);
    }

    private DragDropEffects FolderEffect(DragEventArgs e)
    {
        var data = _dragData ?? e.Data as ComIDataObject;
        if (data is null) return DragDropEffects.None;
        if (_folderTarget is null)
        {
            _folderTarget = ShellDropTarget.For(TargetFolder);
            if (_folderTarget is null) return DragDropEffects.None;
            return _folderTarget.DragEnter(data, e.KeyStates, e.AllowedEffects);
        }
        return _folderTarget.DragOver(e.KeyStates, e.AllowedEffects);
    }

    private DragDropEffects? ItemTargetFor(ListBoxItem container, ItemView item, DragEventArgs e)
    {
        var data = _dragData ?? e.Data as ComIDataObject;
        if (data is null || _noDropTarget.Contains(item.Id)) return null;

        if (!ReferenceEquals(container, _itemTargetContainer))
        {
            LeaveItemTarget();
            var target = ShellDropTarget.For(item.Path);
            if (target is null)
            {
                _noDropTarget.Add(item.Id);
                return null;
            }
            var entered = target.DragEnter(data, e.KeyStates, e.AllowedEffects);
            if (entered == DragDropEffects.None)
            {
                target.Dispose();
                _noDropTarget.Add(item.Id); // dit item neemt deze bestanden niet aan (bijv. een tekstbestand)
                return null;
            }
            _itemTarget = target;
            _itemTargetContainer = container;
            container.Tag = "DropTarget";
            return entered;
        }
        return _itemTarget!.DragOver(e.KeyStates, e.AllowedEffects);
    }

    /// <summary>Alleen het plaatje zelf (en een randje eromheen) is het doel; de rest van de tegel is "ervoor/erna".</summary>
    private static bool IsOverIcon(ListBoxItem container, DragEventArgs e)
    {
        if (Part<FrameworkElement>(container, "Icon") is not { } icon) return false;
        var p = e.GetPosition(icon);
        const double slack = 8;
        return p.X >= -slack && p.Y >= -slack && p.X <= icon.ActualWidth + slack && p.Y <= icon.ActualHeight + slack;
    }

    private void LeaveItemTarget()
    {
        if (_itemTargetContainer is not null) _itemTargetContainer.Tag = null;
        _itemTargetContainer = null;
        _itemTarget?.Dispose(); // meldt het doel ook "weg"
        _itemTarget = null;
    }

    private void LeaveFolderTarget()
    {
        _folderTarget?.Dispose();
        _folderTarget = null;
    }

    private void ResetDrop()
    {
        _dragInside = false;
        _dragData = null;
        LeaveItemTarget();
        LeaveFolderTarget();
        InsertMarker.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// De plek in de eigen volgorde van het pane: vóór het icoon onder de muis, of erna als de muis op de rechterhelft
    /// (in een lijst: de onderste helft) staat. Gesorteerd telt de plek niet: dan -1 (achteraan).
    /// </summary>
    private int DropIndex(DragEventArgs e)
    {
        if (_sorted) return -1;
        if (VisibleDropIndex(e) is not { } visible) return -1;
        // Met een filter zijn niet alle iconen te zien: de plek telt in de hele volgorde.
        if (visible >= _items.Count) return _items.Count == 0 ? -1 : IndexInAll(_items[^1]) + 1;
        return IndexInAll(_items[visible]);
    }

    private int IndexInAll(ItemView item) => _allItems.ToList().FindIndex(i => i.Id == item.Id);

    /// <summary>De plek tussen de zichtbare iconen, of <c>null</c> als de muis niet boven een icoon staat.</summary>
    private int? VisibleDropIndex(DragEventArgs e)
    {
        if (ContainerAt(e.GetPosition(ItemsHost)) is not { DataContext: ItemView target } container) return null;
        var index = _items.ToList().FindIndex(i => i.Id == target.Id);
        if (index < 0) return null;
        var position = e.GetPosition(container);
        var after = _mode == IconSize.List ? position.Y > container.ActualHeight / 2 : position.X > container.ActualWidth / 2;
        return after ? index + 1 : index;
    }

    private void ShowInsertMarker(DragEventArgs e)
    {
        // Gesorteerd bepaalt de sortering de plek; een streep zou iets beloven wat niet gebeurt.
        int? index = _sorted ? null : VisibleDropIndex(e) ?? _items.Count;
        ListBoxItem? container;
        bool after;
        if (index is null) { InsertMarker.Visibility = Visibility.Collapsed; return; }
        if (index < _items.Count) { container = ContainerOf(_items[index.Value]); after = false; }
        else if (_items.Count > 0) { container = ContainerOf(_items[^1]); after = true; }
        else { InsertMarker.Visibility = Visibility.Collapsed; return; }
        if (container is null) { InsertMarker.Visibility = Visibility.Collapsed; return; }

        var bounds = container.TransformToVisual(Overlay).TransformBounds(new Rect(container.RenderSize));
        if (_mode == IconSize.List)
        {
            // Een liggende streep tussen twee regels.
            InsertMarker.Width = Math.Max(0, bounds.Width - 12);
            InsertMarker.Height = 3;
            Canvas.SetLeft(InsertMarker, bounds.Left + 6);
            Canvas.SetTop(InsertMarker, (after ? bounds.Bottom : bounds.Top) - 1.5);
        }
        else
        {
            InsertMarker.Width = 3;
            Canvas.SetLeft(InsertMarker, (after ? bounds.Right + 1 : bounds.Left - 3) - InsertMarker.Width / 2 + 1);
            Canvas.SetTop(InsertMarker, bounds.Top + 6);
            InsertMarker.Height = Math.Max(0, bounds.Height - 12);
        }
        InsertMarker.Visibility = Visibility.Visible;
    }

    // ---------- Openen ----------

    private static ItemView? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as ItemView;

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item) Open(item);
    }

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        if (VirtualDesktopItems.IsVirtual(item.Path)) Open(item); // heeft geen bestandslocatie
        else StartProcess(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\""));
    }

    private void Open(ItemView item)
    {
        if (IsFolderView && Directory.Exists(item.Path))
        {
            FolderOpened?.Invoke(this, item.Path);
            return;
        }
        if (OpenWithShell(item)) ItemOpened?.Invoke(this, item); // mislukt (bijv. kapotte snelkoppeling): niet gebruikt
    }

    /// <summary>Enter op de selectie. In een mappane gaat dat de eerste map in (meer mappen tegelijk kan niet).</summary>
    private void OpenSelection(List<ItemView> selected)
    {
        if (IsFolderView && selected.FirstOrDefault(i => Directory.Exists(i.Path)) is { } folder)
        {
            Open(folder);
            return;
        }
        foreach (var item in selected) Open(item);
    }

    private static bool OpenWithShell(ItemView item) =>
        StartProcess(VirtualDesktopItems.IsVirtual(item.Path)
            ? new ProcessStartInfo("explorer.exe", "shell:" + item.Path)
            : new ProcessStartInfo(item.Path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(item.Path),
            });

    /// <summary>Start een programma of document; <c>false</c> (met een melding) als dat niet lukte.</summary>
    private static bool StartProcess(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info)?.Dispose(); // het proces zelf hoeven we niet te volgen
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Bijv. geannuleerd UAC-venster of verwijderd doel van een snelkoppeling.
            MessageBox.Show(ex.Message, "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>
    /// Een optie uit het menu van Verkenner is uitgevoerd. Openen, bewerken, "Openen met" en dergelijke tellen als
    /// gebruik van de items waar het menu voor gold (die in dezelfde map als het eerste, zie
    /// <see cref="ShellContextMenu.TryShow(IReadOnlyList{string}, Action?, Action{string?}?)"/>).
    /// </summary>
    private void OnShellVerbInvoked(List<ItemView> selected, string? verb)
    {
        if (!ItemUsage.CountsAsUse(verb)) return;
        var folder = Path.GetDirectoryName(selected[0].Path);
        foreach (var item in selected.Where(i => string.Equals(Path.GetDirectoryName(i.Path), folder, StringComparison.OrdinalIgnoreCase)))
            ItemOpened?.Invoke(this, item);
    }
}

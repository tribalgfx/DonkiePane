using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
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

    private static readonly HashSet<string> CutIds = new(StringComparer.OrdinalIgnoreCase);
    private static ComIDataObject? _cutData;
    private static DispatcherTimer? _cutTimer;
    private static event EventHandler? CutChanged;

    private IReadOnlyList<ItemView> _items = [];
    private Point? _dragStart;               // waar de linkerknop op een icoon omlaag ging
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

    /// <summary>De map waar geplakte en hierheen gesleepte bestanden heen gaan: je eigen bureaublad.</summary>
    public static string DesktopFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

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

        var selected = SelectedIds();
        var previous = new HashSet<string>(_items.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);
        var focusedId = (Keyboard.FocusedElement as ListBoxItem)?.DataContext is ItemView f ? f.Id : null;
        _items = items;
        ItemsHost.ItemsSource = items;
        EmptyHint.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (selected.Count > 0) Select(selected);
        if (focusedId is not null && items.FirstOrDefault(i => i.Id == focusedId) is { } focused)
            Dispatcher.InvokeAsync(() => (ItemsHost.ItemContainerGenerator.ContainerFromItem(focused) as ListBoxItem)?.Focus(),
                DispatcherPriority.Loaded);
        Dispatcher.InvokeAsync(ApplyCutState, DispatcherPriority.Loaded);

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
            ShellContextMenu.ShowBackground(DesktopFolder,
                beforeCommand: () => ArrivalExpected?.Invoke(this, EventArgs.Empty),
                newItem: () => _renameNextNewUntil = DateTime.UtcNow.AddSeconds(10));
            return;
        }
        ShowItemMenu(selected);
    }

    private void ShowItemMenu(List<ItemView> selected)
    {
        if (ShellContextMenu.TryShow(PathsOf(selected), rename: () => BeginRename(selected[0]))) return;

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
                foreach (var item in selected) Open(item);
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
                ShellItems.Paste(DesktopFolder);
                break;
            case Key.N when ctrl && shift:
                ArrivalExpected?.Invoke(this, EventArgs.Empty);
                _renameWhenArrives = ShellItems.CreateNewFolder(DesktopFolder);
                break;
            case Key.A when ctrl:
                ItemsHost.SelectAll();
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

    private void OnCutChanged(object? sender, EventArgs e) => ApplyCutState();

    private void ApplyCutState()
    {
        foreach (var item in _items)
            if (ContainerOf(item) is { } container) SetIsCut(container, CutIds.Contains(item.Id));
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
        var p = e.GetPosition(Root);
        if (p.X >= 0 && p.Y >= 0 && p.X < Root.ActualWidth && p.Y < Root.ActualHeight) return;
        ResetDrop();
        DragImages.Leave();
        e.Handled = true;
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        UpdateDrop(e);
        var effect = e.Effects;
        var data = _dragData ?? e.Data as ComIDataObject;

        if (_itemTarget is not null && data is not null)
        {
            // Op een map, de Prullenbak of een programma: dat doet de shell (verplaatsen, verwijderen, openen met).
            effect = _itemTarget.Drop(data, e.KeyStates, e.AllowedEffects);
        }
        else if (ActiveDrag is { } drag)
        {
            ItemsDropped?.Invoke(this, (drag.Ids, DropIndex(e)));
            effect = DragDropEffects.Move;
        }
        else if (e.Data.GetData(InternalDataFormat) is string ids)
        {
            ItemsDropped?.Invoke(this, (ids.Split('|'), DropIndex(e)));
            effect = DragDropEffects.Move;
        }
        else if (_folderTarget is not null && data is not null)
        {
            // Bestanden van buiten: naar het bureaublad, en in dit pane.
            ArrivalExpected?.Invoke(this, EventArgs.Empty);
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
        var internalDrag = ActiveDrag is not null || e.Data.GetDataPresent(InternalDataFormat);
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

        // 3. Bestanden van buiten: wat de bureaubladmap ermee zou doen (verplaatsen, kopiëren, snelkoppeling).
        InsertMarker.Visibility = Visibility.Collapsed;
        e.Effects = FolderEffect(e);
    }

    private DragDropEffects FolderEffect(DragEventArgs e)
    {
        var data = _dragData ?? e.Data as ComIDataObject;
        if (data is null) return DragDropEffects.None;
        if (_folderTarget is null)
        {
            _folderTarget = ShellDropTarget.For(DesktopFolder);
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

    /// <summary>De plek in de lijst: vóór het icoon onder de muis, of erna als de muis op de rechterhelft staat.</summary>
    private int DropIndex(DragEventArgs e)
    {
        if (ContainerAt(e.GetPosition(ItemsHost)) is not { DataContext: ItemView target } container) return -1;
        var index = _items.ToList().FindIndex(i => i.Id == target.Id);
        if (index < 0) return -1;
        return e.GetPosition(container).X > container.ActualWidth / 2 ? index + 1 : index;
    }

    private void ShowInsertMarker(DragEventArgs e)
    {
        var index = DropIndex(e);
        ListBoxItem? container;
        bool after;
        if (index >= 0 && index < _items.Count) { container = ContainerOf(_items[index]); after = false; }
        else if (_items.Count > 0) { container = ContainerOf(_items[^1]); after = true; }
        else { InsertMarker.Visibility = Visibility.Collapsed; return; }
        if (container is null) { InsertMarker.Visibility = Visibility.Collapsed; return; }

        var bounds = container.TransformToVisual(Overlay).TransformBounds(new Rect(container.RenderSize));
        Canvas.SetLeft(InsertMarker, (after ? bounds.Right + 1 : bounds.Left - 3) - InsertMarker.Width / 2 + 1);
        Canvas.SetTop(InsertMarker, bounds.Top + 6);
        InsertMarker.Height = Math.Max(0, bounds.Height - 12);
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

    private static void Open(ItemView item) =>
        StartProcess(VirtualDesktopItems.IsVirtual(item.Path)
            ? new ProcessStartInfo("explorer.exe", "shell:" + item.Path)
            : new ProcessStartInfo(item.Path)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(item.Path),
            });

    private static void StartProcess(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info)?.Dispose(); // het proces zelf hoeven we niet te volgen
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Bijv. geannuleerd UAC-venster of verwijderd doel van een snelkoppeling.
            MessageBox.Show(ex.Message, "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

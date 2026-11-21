using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DonkiePane.Core;
using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>
/// Het venster om een pane heen: titelbalk, verslepen, grootte, oprollen,
/// hernoemen, thema, vastklikken op het raster en de koppeling met het
/// bureaublad. De inhoud (iconen of taken) komt via <see cref="SetContent"/>.
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

    public Pane Pane { get; }

    /// <summary>Of en hoe groot het raster is; wordt tijdens het slepen en bij het loslaten gelezen.</summary>
    public Func<(bool Snap, double Cell)> GridSettings { get; set; } = () => (false, GridSnap.DefaultCell);

    /// <summary>Positie, grootte, titel, thema of status is veranderd en moet bewaard worden.</summary>
    public event EventHandler? PaneChanged;
    public event EventHandler? DeleteRequested;

    /// <summary>Het verplaatsen of vergroten begint/eindigt (om het raster te tonen).</summary>
    public event EventHandler? SizeMoveStarted;
    public event EventHandler? SizeMoveEnded;

    public PaneWindow(Pane pane)
    {
        InitializeComponent();
        Pane = pane;
        DesktopLayer.Attach(this);

        TitleText.Text = pane.Title;
        DeleteMenuItem.Visibility = pane.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        Left = pane.Left;
        Top = pane.Top;
        Width = pane.Width;
        ApplyRolledUp();
        ApplyTheme();
        // De inhoud volgt de afgeronde hoeken (anders steken de titelbalk en iconen bij het scrollen uit de hoek).
        Chrome.SizeChanged += (_, e) => Chrome.Clip = new RectangleGeometry(new Rect(e.NewSize), 9, 9);
        BuildThemeMenu();

        LocationChanged += (_, _) =>
        {
            Pane.Left = Left;
            Pane.Top = Top;
        };
        SizeChanged += (_, _) =>
        {
            Pane.Width = ActualWidth;
            if (!Pane.IsRolledUp) Pane.Height = ActualHeight;
        };
        SourceInitialized += (_, _) =>
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
    }

    public void SetContent(UIElement content) => Body.Content = content;

    /// <summary>Het aantal iconen, gedempt naast de titel; <c>null</c> = niet tonen (takenpane).</summary>
    public void SetItemCount(int? count)
    {
        CountText.Text = count?.ToString() ?? "";
        CountText.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Thema, kleur en transparantie ----------

    private void ApplyTheme() => PaneThemes.Apply(Resources, Pane);

    private void BuildThemeMenu()
    {
        // Alleen de themakleuren vernieuwen; de vaste opties eronder (eigen kleur, transparantie, patroon) blijven staan.
        foreach (var old in ThemeMenu.Items.OfType<MenuItem>().Where(i => i.Tag is string).ToList())
            ThemeMenu.Items.Remove(old);
        var current = PaneThemes.Get(Pane.Theme).Id;
        var hasCustom = PaneThemes.TryParseColor(Pane.CustomColor, out _);
        var index = 0;
        foreach (var theme in PaneThemes.All)
        {
            var item = new MenuItem { Header = Loc.T(theme.Name), IsCheckable = true, IsChecked = !hasCustom && theme.Id == current, Tag = theme.Id };
            item.Click += Theme_Click;
            ThemeMenu.Items.Insert(index++, item);
        }

        TransparencyMenu.Items.Clear();
        var currentPercent = (int)Math.Round((1 - Pane.EffectiveOpacity) * 100);
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

        PatternMenuItem.IsChecked = Pane.ShowPattern;
    }

    private void LookChanged()
    {
        ApplyTheme();
        BuildThemeMenu();
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string id }) return;
        Pane.Theme = id;
        Pane.CustomColor = null; // een gekozen thema vervangt een eigen kleur
        LookChanged();
    }

    private void PickColor_Click(object sender, RoutedEventArgs e)
    {
        var start = PaneThemes.TryParseColor(Pane.CustomColor, out var c) ? c
            : (Color)ColorConverter.ConvertFromString(PaneThemes.Resolve(Pane).Title);
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = System.Drawing.Color.FromArgb(start.R, start.G, start.B),
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        Pane.CustomColor = PaneThemes.ToHex(Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B));
        LookChanged();
    }

    private void Transparency_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: int percent }) return;
        Pane.Opacity = 1 - percent / 100.0;
        LookChanged();
    }

    private void Pattern_Click(object sender, RoutedEventArgs e)
    {
        Pane.ShowPattern = PatternMenuItem.IsChecked;
        LookChanged();
    }

    private void ResetLook_Click(object sender, RoutedEventArgs e)
    {
        Pane.CustomColor = null;
        Pane.Opacity = Pane.DefaultOpacity;
        Pane.ShowPattern = false;
        LookChanged();
    }

    // ---------- Verplaatsen en vergroten, vastklikken op het raster ----------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_ENTERSIZEMOVE:
                _boundsAtMoveStart = new Rect(Left, Top, ActualWidth, ActualHeight);
                SizeMoveStarted?.Invoke(this, EventArgs.Empty);
                break;
            case WM_EXITSIZEMOVE:
                SnapToGrid();
                SizeMoveEnded?.Invoke(this, EventArgs.Empty);
                PaneChanged?.Invoke(this, EventArgs.Empty);
                break;
            case WM_MOVING:
                SnapWhileDragging(lParam, null);
                break;
            case WM_SIZING:
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
        if (resized)
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
        try
        {
            DragMove(); // blokkeert tot de muis wordt losgelaten
        }
        catch (InvalidOperationException)
        {
            return;
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
        ApplyRolledUp();
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyRolledUp()
    {
        Body.Visibility = Pane.IsRolledUp ? Visibility.Collapsed : Visibility.Visible;
        Divider.Visibility = Body.Visibility;
        ResizeMode = Pane.IsRolledUp ? ResizeMode.NoResize : ResizeMode.CanResize;
        Height = Pane.IsRolledUp ? TitleBarHeight : Pane.Height;
        RollButton.Content = Pane.IsRolledUp ? "" : ""; // pijltje omlaag/omhoog
        RollButton.ToolTip = Pane.IsRolledUp ? Loc.T("Uitrollen") : Loc.T("Oprollen");
    }

    private void Roll_Click(object sender, RoutedEventArgs e) => ToggleRolledUp();

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

    /// <summary>De titel bewerken (ook meteen na het maken van een pane door iconen los te laten).</summary>
    public void BeginRename()
    {
        if (IsClosed) return;
        TitleEditor.Text = Pane.Title;
        TitleText.Visibility = Visibility.Collapsed;
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
        TitleText.Visibility = Visibility.Visible;

        var title = TitleEditor.Text.Trim();
        if (!commit || title.Length == 0 || title == Pane.Title) return;
        Pane.Title = title;
        TitleText.Text = title;
        PaneChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Delete_Click(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(this, EventArgs.Empty);
}

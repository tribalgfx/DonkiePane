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
    private const double TitleBarHeight = 34;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;

    private Rect _boundsAtMoveStart;

    public Pane Pane { get; }

    /// <summary>Of en hoe groot het raster is; wordt gelezen op het moment van loslaten.</summary>
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
            var item = new MenuItem { Header = theme.Name, IsCheckable = true, IsChecked = !hasCustom && theme.Id == current, Tag = theme.Id };
            item.Click += Theme_Click;
            ThemeMenu.Items.Insert(index++, item);
        }

        TransparencyMenu.Items.Clear();
        var currentPercent = (int)Math.Round((1 - Pane.EffectiveOpacity) * 100);
        foreach (var percent in PaneThemes.TransparencySteps)
        {
            var item = new MenuItem
            {
                Header = percent == 0 ? "0% (massief)" : $"{percent}%",
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
                break;
        }
        return IntPtr.Zero;
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
            Pane.IsRolledUp = !Pane.IsRolledUp;
            ApplyRolledUp();
            PaneChanged?.Invoke(this, EventArgs.Empty);
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

    private void ApplyRolledUp()
    {
        Body.Visibility = Pane.IsRolledUp ? Visibility.Collapsed : Visibility.Visible;
        ResizeMode = Pane.IsRolledUp ? ResizeMode.NoResize : ResizeMode.CanResizeWithGrip;
        Height = Pane.IsRolledUp ? TitleBarHeight : Pane.Height;
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using DonkiePane.Shell;
using Forms = System.Windows.Forms;

namespace DonkiePane.App;

/// <summary>
/// Een onzichtbaar vlak over het bureaublad van één scherm, alleen zolang je iconen uit een pane sleept. Laat je ze
/// op een lege plek los, dan komt daar een nieuw pane met die iconen. Het ligt op de
/// bureaubladlaag onder de panes; Verkenner, andere vensters en de panes zelf blijven dus gewone doelen.
///
/// Zonder dit vlak zou het bureaublad van Windows de iconen aannemen, maar de bestanden staan daar al: er zou niets
/// zichtbaars gebeuren, en een item van het openbare bureaublad zou naar je eigen bureaublad verhuizen.
/// </summary>
public sealed class DesktopDropWindow : Window
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;

    /// <summary>De grootte van het voorbeeldkader, gelijk aan een nieuw pane.</summary>
    public static readonly Size GhostSize = new(320, 224);

    private readonly Canvas _canvas = new();
    private readonly Border _ghost;
    private bool _inside;

    /// <summary>Losgelaten op een lege plek, met de linkerbovenhoek van het nieuwe pane in schermpixels.</summary>
    public event EventHandler<(int X, int Y)>? Dropped;

    public DesktopDropWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        // Bijna doorzichtig in plaats van helemaal: een volledig doorzichtig venster neemt geen muis of slepen aan.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        AllowDrop = true;

        _ghost = new Border
        {
            Width = GhostSize.Width,
            Height = GhostSize.Height,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1.5),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = Loc.T("Nieuw pane"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)),
            },
        };
        _ghost.SetResourceReference(Border.BackgroundProperty, "GhostFill");
        _ghost.SetResourceReference(Border.BorderBrushProperty, "PaneAccentLine");
        Resources["GhostFill"] = new SolidColorBrush(Color.FromArgb(0x40, 0x10, 0x14, 0x1A));
        Resources["PaneAccentLine"] = new SolidColorBrush(PaneThemes.SystemAccent(lighter: true));
        _canvas.Children.Add(_ghost);
        Content = _canvas;

        DesktopLayer.Attach(this);
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)((long)style | WS_EX_NOACTIVATE));
        };
    }

    /// <summary>Over het hele scherm leggen (fysieke pixels), onder de panes.</summary>
    public void ShowOn(Forms.Screen screen)
    {
        if (!IsVisible) Show();
        var b = screen.Bounds;
        DesktopLayer.SetBounds(this, b.X, b.Y, b.Width, b.Height);
        DesktopLayer.SendToBottom(this);
    }

    public void HideNow()
    {
        _inside = false;
        _ghost.Visibility = Visibility.Collapsed;
        if (IsVisible) Hide();
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        Update(e);
        if (!_inside) DragImages.Enter(Handle, e.Data as System.Runtime.InteropServices.ComTypes.IDataObject, e.Effects);
        _inside = true;
        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        Update(e);
        DragImages.Over(e.Effects);
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        var p = e.GetPosition(this);
        if (p.X >= 0 && p.Y >= 0 && p.X < ActualWidth && p.Y < ActualHeight) return;
        _inside = false;
        _ghost.Visibility = Visibility.Collapsed;
        DragImages.Leave();
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        Update(e);
        var data = e.Data as System.Runtime.InteropServices.ComTypes.IDataObject;
        DragImages.Drop(data, e.Effects);
        _inside = false;
        _ghost.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (e.Effects == DragDropEffects.None) return;

        // De hoek van het voorbeeldkader, omgerekend naar schermpixels.
        var corner = PointToScreen(new Point(Canvas.GetLeft(_ghost), Canvas.GetTop(_ghost)));
        Dropped?.Invoke(this, ((int)Math.Round(corner.X), (int)Math.Round(corner.Y)));
    }

    private void Update(DragEventArgs e)
    {
        if (IconsView.ActiveDrag is null)
        {
            e.Effects = DragDropEffects.None;
            _ghost.Visibility = Visibility.Collapsed;
            return;
        }
        e.Effects = DragDropEffects.Move;
        var p = e.GetPosition(_canvas);
        Canvas.SetLeft(_ghost, p.X - 40);
        Canvas.SetTop(_ghost, p.Y - 20);
        _ghost.Visibility = Visibility.Visible;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using DonkiePane.Core;
using DonkiePane.Shell;
using Forms = System.Windows.Forms;

namespace DonkiePane.App;

/// <summary>
/// Een doorzichtig venster met het ruitjesraster, dat verschijnt terwijl je een
/// pane verplaatst of vergroot. Het hangt op de bureaubladlaag (onder de panes),
/// vangt geen muisklikken en neemt de focus niet over.
/// </summary>
public sealed class GridOverlayWindow : Window
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int GWL_EXSTYLE = -20;

    private readonly System.Windows.Controls.Border _surface = new() { IsHitTestVisible = false };

    public GridOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Content = _surface;

        DesktopLayer.Attach(this);
        SourceInitialized += (_, _) =>
        {
            // Muisklikken gaan dwars door het raster heen.
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        };
    }

    /// <summary>Toont het raster over het scherm waar <paramref name="pane"/> staat.</summary>
    public void ShowFor(Window pane, double cell)
    {
        var paneHandle = new WindowInteropHelper(pane).Handle;
        var screen = Forms.Screen.FromHandle(paneHandle);
        var bounds = screen.Bounds; // fysieke pixels

        // Panes rekenen in eenheden van hún scherm; het raster moet daar precies
        // op aansluiten, ook als dat scherm niet op x=0 begint.
        var dpi = VisualTreeHelper.GetDpi(pane);
        var originX = bounds.X / dpi.DpiScaleX;
        var originY = bounds.Y / dpi.DpiScaleY;
        _surface.Background = CreateGrid(cell, GridSnap.Offset(originX, cell), GridSnap.Offset(originY, cell));

        if (!IsVisible) Show();
        DesktopLayer.SetBounds(this, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        DesktopLayer.SendToBottom(this); // onder de panes, zodat het raster niet over hun inhoud ligt
    }

    private static Brush CreateGrid(double cell, double offsetX, double offsetY)
    {
        var line = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        line.Freeze();

        // Eén cel: een lijn links en een lijn boven.
        var tile = new DrawingGroup();
        tile.Children.Add(new GeometryDrawing(line, null, new RectangleGeometry(new Rect(0, 0, 1, cell))));
        tile.Children.Add(new GeometryDrawing(line, null, new RectangleGeometry(new Rect(0, 0, cell, 1))));

        var brush = new DrawingBrush(tile)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(offsetX, offsetY, cell, cell),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, cell, cell),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
}

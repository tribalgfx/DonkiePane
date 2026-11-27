using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>
/// Een donker, half doorzichtig vlak over een heel scherm terwijl de panes naar voren gehaald zijn. Klikken of Esc
/// zet de panes terug op het bureaublad.
/// </summary>
internal sealed class RaiseOverlayWindow : Window
{
    /// <summary>Er is op het vlak geklikt of Esc ingedrukt.</summary>
    public event EventHandler? Dismissed;

    public RaiseOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromArgb(0x73, 0x0A, 0x0B, 0x0D));
        Title = "DonkiePane";
        SourceInitialized += (_, _) =>
        {
            // Niet in Alt+Tab.
            var hwnd = new WindowInteropHelper(this).Handle;
            const int GWL_EXSTYLE = -20;
            const long WS_EX_TOOLWINDOW = 0x80;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr((long)GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW));
        };
        MouseDown += (_, _) => Dismissed?.Invoke(this, EventArgs.Empty);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Dismissed?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Over dit scherm (fysieke pixels), met een korte vervaging in beeld.</summary>
    public void ShowOn(System.Drawing.Rectangle bounds)
    {
        BeginAnimation(OpacityProperty, null); // de vorige animatie houdt anders de doorzichtigheid vast
        Opacity = 0;
        Left = -32000; // eerst buiten beeld; de echte plek in pixels hieronder (los van de schaal van het scherm)
        Top = -32000;
        Width = 100;
        Height = 100;
        Show();
        DesktopLayer.SetBounds(this, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(140)));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}

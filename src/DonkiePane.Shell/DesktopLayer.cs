using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static DonkiePane.Shell.NativeMethods;

namespace DonkiePane.Shell;

/// <summary>
/// Zet WPF-vensters op de bureaubladlaag.
///
/// Werking: het venster krijgt als <i>eigenaar</i> het bureaubladvenster van
/// Verkenner (het topniveauvenster met daarin <c>SHELLDLL_DefView</c>, waar de
/// iconen in staan). Windows houdt een venster altijd boven zijn eigenaar, en
/// wanneer Win+D het bureaublad naar voren haalt, komen de panes mee.
/// Daarnaast wordt elke poging om het venster naar voren te halen omgebogen
/// naar "onderaan", zodat de panes altijd achter gewone vensters blijven.
///
/// Windows heeft hier geen officiële API voor; als een Windows-update de
/// opbouw van het bureaublad verandert, hoeft alleen deze klasse aangepast.
/// </summary>
public static class DesktopLayer
{
    /// <summary>
    /// Zoekt het topniveauvenster dat de bureaubladiconen bevat. Normaal is dat
    /// <c>Progman</c>; als een achtergrondprogramma (bijv. Wallpaper Engine) de
    /// iconen naar een <c>WorkerW</c> heeft verplaatst, is het die.
    /// </summary>
    public static IntPtr FindDesktopWindow()
    {
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero && FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            return progman;

        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);

        return found != IntPtr.Zero ? found : progman;
    }

    /// <summary>
    /// Koppelt het venster aan de bureaubladlaag. Aanroepen vóór <c>Show()</c>.
    /// Geeft <c>false</c> als het bureaubladvenster niet gevonden is; het venster
    /// werkt dan als gewoon venster.
    /// </summary>
    public static bool Attach(Window window)
    {
        var desktop = FindDesktopWindow();
        if (desktop == IntPtr.Zero) return false;

        var helper = new WindowInteropHelper(window);
        helper.Owner = desktop;

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = helper.Handle;

            // Niet in Alt+Tab of de taakbalk.
            var exStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
            exStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

            HwndSource.FromHwnd(hwnd)?.AddHook(KeepAtBottom);
        };

        window.Loaded += (_, _) => SendToBottom(helper.Handle);
        return true;
    }

    /// <summary>Waar het venster nu staat, in fysieke schermpixels (niet in WPF-eenheden).</summary>
    public static DonkiePane.Core.PxRect? GetBounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
        return new DonkiePane.Core.PxRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
    }

    /// <summary>Zet een venster op een positie in fysieke schermpixels, zonder het te activeren of de stapelvolgorde te wijzigen.</summary>
    public static void SetBounds(Window window, int x, int y, int width, int height)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Legt het venster onderaan, direct boven het bureaublad (onder alle andere panes).</summary>
    public static void SendToBottom(Window window) => SendToBottom(new WindowInteropHelper(window).Handle);

    private static void SendToBottom(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>
    /// De panes staan even boven alle vensters (de sneltoets om naar je panes te kijken). Zolang dit aan staat, worden
    /// ze niet naar onderen gestuurd als je erop klikt.
    /// </summary>
    public static bool Peeking { get; private set; }

    /// <summary>Haalt deze vensters boven alle andere (ook boven vensters die "altijd bovenop" staan, als laatste gezet).</summary>
    public static void BeginPeek(IEnumerable<Window> windows)
    {
        Peeking = true;
        foreach (var window in windows)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    /// <summary>Zet de vensters weer gewoon op de bureaubladlaag.</summary>
    public static void EndPeek(IEnumerable<Window> windows)
    {
        Peeking = false;
        foreach (var window in windows)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) continue;
            SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            SendToBottom(hwnd);
        }
    }

    /// <summary>Maakt dit venster het actieve (mag direct na een sneltoets).</summary>
    public static void Activate(Window window) => SetForegroundWindow(new WindowInteropHelper(window).Handle);

    /// <summary>
    /// Begint het verslepen van een venster aan de titelbalk alsof je hem net hebt vastgepakt, terwijl de muisknop nog
    /// ingedrukt is (bijv. een tabblad dat je uit een pane trekt en dat een eigen venster wordt).
    /// </summary>
    public static void BeginDragMove(Window window)
    {
        const int WM_NCLBUTTONDOWN = 0x00A1, HTCAPTION = 2;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        ReleaseCapture();
        GetCursorPos(out var pt);
        var lParam = new IntPtr((pt.Y << 16) | (pt.X & 0xFFFF));
        PostMessage(hwnd, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), lParam);
    }

    /// <summary>Waar de muis nu is, in fysieke schermpixels.</summary>
    public static System.Drawing.Point CursorPosition()
    {
        GetCursorPos(out var pt);
        return new System.Drawing.Point(pt.X, pt.Y);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    private static unsafe IntPtr KeepAtBottom(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING && !Peeking)
        {
            var pos = (WINDOWPOS*)lParam;
            // Elke wijziging in de stapelvolgorde (klikken, activeren) wordt
            // "onderaan". Omdat het bureaublad de eigenaar is, blijft het pane
            // daar net boven.
            if ((pos->flags & SWP_NOZORDER) == 0)
                pos->hwndInsertAfter = HWND_BOTTOM;
        }
        return IntPtr.Zero;
    }
}

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

    private static void SendToBottom(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    private static unsafe IntPtr KeepAtBottom(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
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

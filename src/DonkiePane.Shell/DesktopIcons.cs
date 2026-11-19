using static DonkiePane.Shell.NativeMethods;

namespace DonkiePane.Shell;

/// <summary>
/// Toont of verbergt de eigen iconen van het Windows-bureaublad.
///
/// Dit gebruikt precies dezelfde opdracht als rechtsklikken op het bureaublad →
/// Beeld → Bureaubladpictogrammen weergeven. Dat heeft een belangrijk voordeel:
/// gaat er iets mis met DonkiePane, dan haal je de iconen altijd zelf terug via
/// dat menu.
/// </summary>
public static class DesktopIcons
{
    private const int WM_COMMAND = 0x0111;
    private const int ToggleDesktopIconsCommand = 0x7402;

    /// <summary>Verkenner wordt niet langer opgehouden dan dit; daarna gaat DonkiePane (bijv. het afsluiten) gewoon door.</summary>
    private const int ExplorerTimeoutMs = 3000;

    /// <summary>Is het bureaublad van Verkenner er (bij het inloggen start DonkiePane soms eerder)?</summary>
    public static bool IsDesktopReady() => FindDefView() != IntPtr.Zero;

    /// <summary>
    /// <c>null</c> als het niet te zeggen is: geen bureaublad, of (nog) geen iconenweergave, bijvoorbeeld tijdens een
    /// herstart van Verkenner. Dan mag er ook niets geschakeld worden, want de opdracht is een schakelaar.
    /// </summary>
    public static bool? AreVisible()
    {
        var defView = FindDefView();
        if (defView == IntPtr.Zero) return null;
        var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        return listView == IntPtr.Zero ? null : IsWindowVisible(listView);
    }

    public static void SetVisible(bool visible)
    {
        var current = AreVisible();
        if (current is null || current == visible) return;
        // Een hangende Verkenner mag DonkiePane (en daarmee het afsluiten voor een update) niet laten vastlopen.
        SendMessageTimeout(FindDefView(), WM_COMMAND, new IntPtr(ToggleDesktopIconsCommand), IntPtr.Zero,
            SMTO_ABORTIFHUNG, ExplorerTimeoutMs, out _);
    }

    private static IntPtr FindDefView()
    {
        var desktop = DesktopLayer.FindDesktopWindow();
        return desktop == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(desktop, IntPtr.Zero, "SHELLDLL_DefView", null);
    }
}

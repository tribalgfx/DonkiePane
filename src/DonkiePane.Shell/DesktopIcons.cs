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

    /// <summary><c>null</c> als het bureaublad niet gevonden is.</summary>
    public static bool? AreVisible()
    {
        var defView = FindDefView();
        if (defView == IntPtr.Zero) return null;
        var listView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
        return listView != IntPtr.Zero && IsWindowVisible(listView);
    }

    public static void SetVisible(bool visible)
    {
        var current = AreVisible();
        if (current is null || current == visible) return;
        SendMessage(FindDefView(), WM_COMMAND, new IntPtr(ToggleDesktopIconsCommand), IntPtr.Zero);
    }

    private static IntPtr FindDefView()
    {
        var desktop = DesktopLayer.FindDesktopWindow();
        return desktop == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(desktop, IntPtr.Zero, "SHELLDLL_DefView", null);
    }
}

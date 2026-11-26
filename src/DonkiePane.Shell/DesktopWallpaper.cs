using System.IO;
using System.Runtime.InteropServices;

namespace DonkiePane.Shell;

/// <summary>Hoe een achtergrondafbeelding op een scherm staat (zoals in Instellingen → Achtergrond → "Aanpassen").</summary>
public enum WallpaperFit
{
    Center = 0,
    Tile = 1,
    Stretch = 2,
    Fit = 3,
    Fill = 4,
    Span = 5,
}

/// <summary>De achtergrond van één scherm: welke afbeelding, hoe die past, en de kleur eromheen (alles in fysieke pixels).</summary>
public sealed record MonitorWallpaper(int X, int Y, int Width, int Height, string? ImagePath, WallpaperFit Fit, uint BackgroundRgb);

/// <summary>
/// Leest de bureaubladachtergrond van elk scherm via de shell (IDesktopWallpaper). Alleen lezen; DonkiePane verandert
/// nooit je achtergrond.
/// </summary>
public static class DesktopWallpaper
{
    public static IReadOnlyList<MonitorWallpaper> Read()
    {
        var result = new List<MonitorWallpaper>();
        IDesktopWallpaper? wallpaper = null;
        try
        {
            wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            wallpaper.GetPosition(out var fit);
            wallpaper.GetBackgroundColor(out var color); // 0x00BBGGRR
            var rgb = ((color & 0xFF) << 16) | (color & 0xFF00) | ((color >> 16) & 0xFF);
            var fallback = TranscodedWallpaper();

            wallpaper.GetMonitorDevicePathCount(out var count);
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    wallpaper.GetMonitorDevicePathAt(i, out var monitorId);
                    wallpaper.GetMonitorRECT(monitorId, out var rect);
                    string? path = null;
                    try { wallpaper.GetWallpaper(monitorId, out path); }
                    catch (COMException) { }
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = fallback; // Windows Spotlight, diavoorstelling
                    result.Add(new MonitorWallpaper(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, path, (WallpaperFit)fit, rgb));
                }
                catch (COMException)
                {
                    // Een scherm dat net wegvalt.
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            // Geen achtergrond te lezen: de panes worden effen.
        }
        finally
        {
            if (wallpaper is not null) Marshal.ReleaseComObject(wallpaper);
        }
        return result;
    }

    /// <summary>De afbeelding die Windows als huidige achtergrond bewaart (ook bij Spotlight en een diavoorstelling).</summary>
    private static string? TranscodedWallpaper()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return File.Exists(path) ? path : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
    private class DesktopWallpaperClass { }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitorId, [MarshalAs(UnmanagedType.LPWStr)] out string wallpaper);
        void GetMonitorDevicePathAt(uint monitorIndex, [MarshalAs(UnmanagedType.LPWStr)] out string monitorId);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId, out Rect displayRect);
        void SetBackgroundColor(uint color);
        void GetBackgroundColor(out uint color);
        void SetPosition(int position);
        void GetPosition(out int position);
        // De rest (diavoorstelling, aan/uit) is niet nodig.
    }
}

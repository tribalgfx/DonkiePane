using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DonkiePane.Shell;

/// <summary>
/// Haalt naam en icoon van een bestand op zoals Verkenner ze toont: zonder
/// <c>.lnk</c>, met het icoon van het doel van een snelkoppeling, en met
/// miniaturen voor afbeeldingen.
/// </summary>
public static class ShellItemInfo
{
    public static string GetDisplayName(string path)
    {
        IShellItem? item = null;
        try
        {
            item = CreateItem(path);
            item.GetDisplayName(SIGDN_NORMALDISPLAY, out var namePtr);
            try { return Marshal.PtrToStringUni(namePtr) ?? Path.GetFileName(path); }
            finally { Marshal.FreeCoTaskMem(namePtr); }
        }
        // Een bestand of map die (even) weg is of niet toegankelijk: Windows geeft dan ook IOException-soorten
        // (bijv. DirectoryNotFoundException) of UnauthorizedAccessException.
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Path.GetFileNameWithoutExtension(path);
        }
        finally
        {
            Release(item);
        }
    }

    /// <summary>Icoon van <paramref name="pixelSize"/> fysieke pixels, of <c>null</c> als het niet lukt.</summary>
    public static BitmapSource? GetIcon(string path, int pixelSize)
    {
        IShellItem? item = null;
        try
        {
            item = CreateItem(path);
            var factory = (IShellItemImageFactory)item;
            var hr = factory.GetImage(new SIZE { cx = pixelSize, cy = pixelSize }, SIIGBF_RESIZETOFIT, out var hbitmap);
            if (hr < 0 || hbitmap == IntPtr.Zero) return null;
            try { return ToBitmapSource(hbitmap); }
            finally { DeleteObject(hbitmap); }
        }
        catch (Exception ex) when (ex is COMException or IOException or UnauthorizedAccessException or ArgumentException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            Release(item);
        }
    }

    /// <summary>
    /// Meteen loslaten in plaats van wachten op de garbage collector: een icoon- of miniatuurhandler houdt het
    /// bestand anders soms open (en dan kun je het niet verplaatsen of verwijderen).
    /// </summary>
    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
    }

    private static IShellItem CreateItem(string path)
    {
        var iid = typeof(IShellItem).GUID;
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
        return (IShellItem)item;
    }

    /// <summary>
    /// Zet een 32-bits HBITMAP om naar een WPF-afbeelding met behoud van
    /// transparantie (de standaard WPF-omzetting gooit het alfakanaal weg).
    /// </summary>
    private static BitmapSource? ToBitmapSource(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<BITMAP>(), out var bmp) == 0) return null;
        int width = bmp.bmWidth, height = bmp.bmHeight;

        var info = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // negatief = van boven naar beneden
            biPlanes = 1,
            biBitCount = 32,
        };
        var pixels = new byte[width * height * 4];
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(hdc, hbitmap, 0, (uint)height, pixels, ref info, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        // Sommige (oude) iconen hebben helemaal geen alfakanaal: dan ondoorzichtig tonen.
        var hasAlpha = false;
        for (var i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) { hasAlpha = true; break; }

        var format = hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
        var source = BitmapSource.Create(width, height, 96, 96, format, null, pixels, width * 4);
        source.Freeze();
        return source;
    }

    private const uint SIGDN_NORMALDISPLAY = 0;
    private const int SIIGBF_RESIZETOFIT = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx; public int cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr h, int size, out BITMAP bmp);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bmi, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr h);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
}

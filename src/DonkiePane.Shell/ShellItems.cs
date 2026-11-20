using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows;
using static DonkiePane.Shell.ShellNative;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DonkiePane.Shell;

/// <summary>
/// Wat je op het bureaublad met bestanden en snelkoppelingen doet, via de shell van Windows zelf: slepen, kopiëren,
/// knippen, naam wijzigen. Zo gedraagt een pane zich precies als het bureaublad (zelfde meldingen, Prullenbak,
/// ongedaan maken in Verkenner). Alles aanroepen op de UI-thread (STA).
/// </summary>
public static class ShellItems
{
    private const uint DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2;

    /// <summary>
    /// Het data-object dat Verkenner zelf ook maakt voor deze items (bestandslijst, snelkoppeling, ...). Elke app
    /// en elke map die bestanden aanneemt, begrijpt het. <c>null</c> als de shell de items niet kent.
    /// </summary>
    public static ComIDataObject? CreateDataObject(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return null;
        var pidls = new List<IntPtr>();
        try
        {
            foreach (var path in paths)
            {
                if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) < 0) return null;
                pidls.Add(pidl);
            }
            if (SHCreateShellItemArrayFromIDLists((uint)pidls.Count, pidls.ToArray(), out var arrayPtr) < 0) return null;
            var array = TakeObject<IShellItemArray>(arrayPtr);
            try
            {
                var bhid = BHID_DataObject;
                var iid = IID_IDataObject;
                if (array.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out var dataPtr) >= 0 && dataPtr != IntPtr.Zero)
                    return TakeObject<ComIDataObject>(dataPtr);
            }
            finally
            {
                Marshal.ReleaseComObject(array);
            }

            // Terugval: items uit één map, zoals Verkenner het in een map doet.
            using var items = new ItemsInFolder(paths);
            return items.GetUIObject<ComIDataObject>(IntPtr.Zero, IID_IDataObject);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidCastException)
        {
            return null;
        }
        finally
        {
            foreach (var pidl in pidls) Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>
    /// Sleept de items met de shell: Verkenner, het bureaublad, andere apps en de panes zelf kunnen ze aannemen.
    /// Blokkeert tot de muis wordt losgelaten. Geeft terug wat het doel heeft gedaan.
    /// </summary>
    public static DragDropEffects DoDragDrop(ComIDataObject data, DragDropEffects allowed)
    {
        SetDefaultDragImage(data);
        var hr = SHDoDragDrop(IntPtr.Zero, data, IntPtr.Zero, (uint)allowed, out var effect);
        return hr < 0 ? DragDropEffects.None : (DragDropEffects)effect;
    }

    /// <summary>Het sleepplaatje dat Verkenner ook toont (iconen met een aantal), als de shell het kan maken.</summary>
    private static void SetDefaultDragImage(ComIDataObject data)
    {
        if (Create<IDragSourceHelper>(CLSID_DragDropHelper) is not { } helper) return;
        try
        {
            var pt = new POINT();
            helper.InitializeFromWindow(IntPtr.Zero, ref pt, data);
        }
        catch (COMException) { }
        finally
        {
            Marshal.ReleaseComObject(helper);
        }
    }

    /// <summary>
    /// Kopiëren of knippen (Ctrl+C, Ctrl+X): daarna kun je ze in Verkenner of elders plakken. Geeft het data-object
    /// op het klembord terug (zie <see cref="IsOnClipboard"/>), of <c>null</c> als het niet lukte.
    /// </summary>
    public static ComIDataObject? CopyToClipboard(IReadOnlyList<string> paths, bool cut)
    {
        if (CreateDataObject(paths) is not { } data) return null;
        try
        {
            SetDword(data, "Preferred DropEffect", cut ? DROPEFFECT_MOVE : DROPEFFECT_COPY);
            return OleSetClipboard(data) >= 0 ? data : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Staat dit data-object nog op het klembord (niet vervangen door iets anders)?</summary>
    public static bool IsOnClipboard(ComIDataObject data) => OleIsCurrentClipboard(data) == 0;

    /// <summary>Staan er bestanden of shell-items op het klembord (gekopieerd of geknipt in Verkenner of een pane)?</summary>
    public static bool CanPaste()
    {
        if (OleGetClipboard(out var data) < 0 || data is null) return false;
        try { return HasFormat(data, CF_HDROP) || HasFormat(data, unchecked((short)RegisterClipboardFormat("Shell IDList Array"))); }
        finally { Marshal.ReleaseComObject(data); }
    }

    /// <summary>
    /// Plakken in <paramref name="folder"/> (Ctrl+V), zoals Verkenner het doet: het klembord wordt neergezet op de
    /// map. Geknipt = verplaatsen, en daarna is het klembord leeg. <paramref name="asLink"/>: snelkoppeling plakken.
    /// </summary>
    public static bool Paste(string folder, bool asLink = false)
    {
        if (OleGetClipboard(out var data) < 0 || data is null) return false;
        try
        {
            var cut = (GetDword(data, "Preferred DropEffect") & DROPEFFECT_MOVE) != 0;
            var effect = asLink ? DragDropEffects.Link : cut ? DragDropEffects.Move : DragDropEffects.Copy;
            using var target = ShellDropTarget.For(folder);
            if (target is null) return false;
            var done = target.SimulateDrop(data, effect);
            if (cut && done == DragDropEffects.Move) OleSetClipboard(null); // geplakt: het geknipte is weg, zoals in Verkenner
            return done != DragDropEffects.None;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            Marshal.ReleaseComObject(data);
        }
    }

    private static FORMATETC Format(short format) => new()
    {
        cfFormat = format,
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        tymed = TYMED.TYMED_HGLOBAL,
    };

    private static bool HasFormat(ComIDataObject data, short format)
    {
        var f = Format(format);
        return data.QueryGetData(ref f) == 0;
    }

    private static uint GetDword(ComIDataObject data, string format)
    {
        var f = Format(unchecked((short)RegisterClipboardFormat(format)));
        if (data.QueryGetData(ref f) != 0) return 0;
        try
        {
            data.GetData(ref f, out var medium);
            try
            {
                var locked = GlobalLock(medium.unionmember);
                try { return locked == IntPtr.Zero ? 0 : unchecked((uint)Marshal.ReadInt32(locked)); }
                finally { GlobalUnlock(medium.unionmember); }
            }
            finally
            {
                ReleaseStgMedium(ref medium);
            }
        }
        catch (COMException)
        {
            return 0;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    /// <summary>Een nieuwe map in <paramref name="folder"/> met een vrije naam ("Nieuwe map", "Nieuwe map (2)", ...).</summary>
    public static string? CreateNewFolder(string folder)
    {
        try
        {
            for (var i = 1; i < 1000; i++)
            {
                var path = Path.Combine(folder, i == 1 ? "Nieuwe map" : $"Nieuwe map ({i})");
                if (Directory.Exists(path) || File.Exists(path)) continue;
                Directory.CreateDirectory(path);
                return path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static void SetDword(ComIDataObject data, string format, uint value)
    {
        var format_ = new FORMATETC
        {
            cfFormat = unchecked((short)RegisterClipboardFormat(format)),
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };
        var memory = Marshal.AllocHGlobal(sizeof(uint));
        Marshal.WriteInt32(memory, unchecked((int)value));
        var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = memory };
        try
        {
            data.SetData(ref format_, ref medium, true); // het data-object ruimt het geheugen nu op
        }
        catch
        {
            Marshal.FreeHGlobal(memory);
            throw;
        }
    }

    /// <summary>
    /// De naam zoals je hem in Verkenner bewerkt: zonder verborgen extensie, zonder ".lnk" bij een snelkoppeling.
    /// <c>null</c> als het item geen naam heeft die je kunt wijzigen.
    /// </summary>
    public static string? GetEditingName(string path)
    {
        if (SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _) < 0) return null;
        try
        {
            if (SHGetNameFromIDList(pidl, SIGDN_PARENTRELATIVEEDITING, out var namePtr) < 0) return null;
            try { return Marshal.PtrToStringUni(namePtr); }
            finally { Marshal.FreeCoTaskMem(namePtr); }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>
    /// Wijzigt de naam zoals Verkenner dat doet (een verborgen extensie blijft staan). Bij een fout, zoals een
    /// ongeldig teken of een bestaande naam, toont de shell zelf een melding. Geeft <c>true</c> als het gelukt is.
    /// </summary>
    public static bool Rename(string path, string newName)
    {
        try
        {
            using var item = new ItemsInFolder([path]);
            var hr = item.Folder.SetNameOf(ShellContextMenu.OwnerHandle, item.Children[0], newName,
                SHGDN_INFOLDER | SHGDN_FOREDITING, out var newPidl);
            if (newPidl != IntPtr.Zero) Marshal.FreeCoTaskMem(newPidl);
            return hr >= 0;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or IOException)
        {
            return false;
        }
    }
}

using System.Runtime.InteropServices;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DonkiePane.Shell;

/// <summary>De COM-interfaces en functies van de Windows-shell die DonkiePane gebruikt (menu, slepen, klembord, naam wijzigen).</summary>
internal static class ShellNative
{
    public const uint SHGDN_INFOLDER = 0x1;
    public const uint SHGDN_FOREDITING = 0x1000;
    public const uint SIGDN_PARENTRELATIVEEDITING = 0x80031001;

    public static Guid IID_IShellFolder = typeof(IShellFolder).GUID;
    public static Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");
    public static Guid IID_IDataObject = new("0000010e-0000-0000-C000-000000000046");
    public static Guid IID_IDropTarget = typeof(IDropTarget).GUID;
    public static Guid BHID_DataObject = new("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4");
    public static readonly Guid CLSID_DragDropHelper = new("4657278A-411B-11D2-839A-00C04FD918D0");
    public static readonly Guid CLSID_NewMenu = new("D969A300-E7FF-11D0-A93B-00A0C90F2719");
    public const short CF_HDROP = 15;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, IntPtr name, IntPtr eaten, IntPtr pidl, IntPtr attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint flags, IntPtr enumIdList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwnd, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, IntPtr attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref Guid riid, IntPtr reserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr pidlOut);
    }

    [ComImport, Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemArray
    {
        // Alleen BindToHandler wordt gebruikt; de rest staat er voor de juiste volgorde.
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyStore(int flags, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetPropertyDescriptionList(IntPtr keyType, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributes(int attribFlags, uint sfgaoMask, out uint attribs);
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetItemAt(uint index, out IntPtr item);
        [PreserveSig] int EnumItems(out IntPtr enumItems);
    }

    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTarget
    {
        [PreserveSig] int DragEnter(ComIDataObject data, uint keyState, POINT pt, ref uint effect);
        [PreserveSig] int DragOver(uint keyState, POINT pt, ref uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop(ComIDataObject data, uint keyState, POINT pt, ref uint effect);
    }

    [ComImport, Guid("DE5BF786-477A-11D2-839D-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDragSourceHelper
    {
        [PreserveSig] int InitializeFromBitmap(IntPtr dragImage, ComIDataObject data);
        [PreserveSig] int InitializeFromWindow(IntPtr hwnd, ref POINT pt, ComIDataObject data);
    }

    [ComImport, Guid("4657278B-411B-11D2-839A-00C04FD918D0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTargetHelper
    {
        [PreserveSig] int DragEnter(IntPtr hwndTarget, ComIDataObject data, ref POINT pt, uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int DragOver(ref POINT pt, uint effect);
        [PreserveSig] int Drop(ComIDataObject data, ref POINT pt, uint effect);
        [PreserveSig] int Show([MarshalAs(UnmanagedType.Bool)] bool show);
    }

    [ComImport, Guid("000214E8-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellExtInit
    {
        [PreserveSig] int Initialize(IntPtr pidlFolder, IntPtr dataObject, IntPtr hkeyProgId);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(string name, IntPtr pbc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    public static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr pidlLast);

    [DllImport("shell32.dll")]
    public static extern int SHGetDesktopFolder(out IntPtr folder);

    [DllImport("shell32.dll")]
    public static extern int SHCreateShellItemArrayFromIDLists(uint count, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls, out IntPtr array);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdn, out IntPtr name);

    [DllImport("shell32.dll")]
    public static extern int SHDoDragDrop(IntPtr hwnd, ComIDataObject data, IntPtr dropSource, uint allowedEffects, out uint effect);

    [DllImport("ole32.dll")]
    public static extern int OleSetClipboard(ComIDataObject? data);

    [DllImport("ole32.dll")]
    public static extern int OleIsCurrentClipboard(ComIDataObject data);

    [DllImport("ole32.dll")]
    public static extern int OleGetClipboard(out ComIDataObject data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClipboardFormat(string format);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT pt);

    /// <summary>Maakt een COM-object en geeft het gevraagde interface, of <c>null</c>.</summary>
    public static T? Create<T>(Guid clsid) where T : class
    {
        var type = Type.GetTypeFromCLSID(clsid, throwOnError: false);
        if (type is null) return null;
        try { return Activator.CreateInstance(type) as T; }
        catch (COMException) { return null; }
    }

    /// <summary>Haalt een interface van een COM-pointer op en laat de pointer los.</summary>
    public static T TakeObject<T>(IntPtr ptr)
    {
        try { return (T)Marshal.GetObjectForIUnknown(ptr); }
        finally { Marshal.Release(ptr); }
    }

    /// <summary>De bovenste map van de shell (het bureaublad als naamruimte).</summary>
    public static IShellFolder DesktopFolder()
    {
        Marshal.ThrowExceptionForHR(SHGetDesktopFolder(out var ptr));
        return TakeObject<IShellFolder>(ptr);
    }

    /// <summary>De shell-map van een pad (een gewone map of bijv. het bureaublad).</summary>
    public static IShellFolder BindToFolder(string path)
    {
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _));
        try
        {
            var desktop = DesktopFolder();
            try
            {
                var iid = IID_IShellFolder;
                Marshal.ThrowExceptionForHR(desktop.BindToObject(pidl, IntPtr.Zero, ref iid, out var ptr));
                return TakeObject<IShellFolder>(ptr);
            }
            finally
            {
                Marshal.ReleaseComObject(desktop);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>
    /// Een aantal items in dezelfde map: de map zelf plus de (relatieve) ID's van de items, voor
    /// <see cref="IShellFolder.GetUIObjectOf"/>. Na gebruik <see cref="Dispose"/> aanroepen.
    /// </summary>
    public sealed class ItemsInFolder : IDisposable
    {
        private readonly List<IntPtr> _absolute = [];

        public IShellFolder Folder { get; }
        public IntPtr[] Children { get; }

        /// <exception cref="COMException">Als de shell een item niet kent.</exception>
        public ItemsInFolder(IReadOnlyList<string> paths)
        {
            if (paths.Count == 0) throw new ArgumentException("Geen items.", nameof(paths));
            try
            {
                var children = new IntPtr[paths.Count];
                IShellFolder? folder = null;
                for (var i = 0; i < paths.Count; i++)
                {
                    Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[i], IntPtr.Zero, out var pidl, 0, out _));
                    _absolute.Add(pidl);
                    var iid = IID_IShellFolder;
                    Marshal.ThrowExceptionForHR(SHBindToParent(pidl, ref iid, out var folderPtr, out var child));
                    if (folder is null) folder = TakeObject<IShellFolder>(folderPtr);
                    else Marshal.Release(folderPtr); // de items staan in dezelfde map; die van het eerste is genoeg
                    children[i] = child; // wijst in pidl, hoeft niet apart vrij
                }
                Folder = folder!;
                Children = children;
            }
            catch
            {
                Free();
                throw;
            }
        }

        public T? GetUIObject<T>(IntPtr owner, Guid iid) where T : class
        {
            var hr = Folder.GetUIObjectOf(owner, (uint)Children.Length, Children, ref iid, IntPtr.Zero, out var ptr);
            return hr < 0 || ptr == IntPtr.Zero ? null : TakeObject<T>(ptr);
        }

        private void Free()
        {
            foreach (var pidl in _absolute) Marshal.FreeCoTaskMem(pidl);
            _absolute.Clear();
        }

        public void Dispose()
        {
            if (Folder is not null) Marshal.ReleaseComObject(Folder);
            Free();
        }
    }

    /// <summary>Verdeelt paden per map: shell-opdrachten werken op items die in één map staan.</summary>
    public static IEnumerable<IReadOnlyList<string>> GroupByFolder(IEnumerable<string> paths) =>
        paths.GroupBy(p => VirtualDesktopItems.IsVirtual(p) ? "::" : System.IO.Path.GetDirectoryName(p) ?? "",
                StringComparer.OrdinalIgnoreCase)
            .Select(g => (IReadOnlyList<string>)g.ToList());
}

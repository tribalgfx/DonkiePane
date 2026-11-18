using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace DonkiePane.Shell;

/// <summary>
/// Het rechtermuisknopmenu van Verkenner voor één bureaubladitem: dezelfde
/// opties als op het bureaublad (met "Meer opties weergeven"), zoals
/// Prullenbak leegmaken, Openen met, Kopiëren, Verwijderen en Eigenschappen.
/// Met Shift erbij komen ook de uitgebreide opties (bijv. Kopiëren als pad).
///
/// "Naam wijzigen" ontbreekt: dat werkt alleen in een Verkenner-venster.
///
/// Het menu en de vensters die een optie opent (bevestiging, Eigenschappen)
/// hangen aan een onzichtbaar hulpvenster. De panes zelf kunnen dat niet zijn:
/// die worden steeds onderaan gelegd, en hun dialogen zouden meegaan.
/// </summary>
public sealed class ShellContextMenu : IDisposable
{
    private const uint FirstCommand = 1;
    private const uint LastCommand = 0x7FFF;

    private static HwndSource? _helper;
    private static ShellContextMenu? _active;

    private readonly IContextMenu _menu;
    private readonly IntPtr _hmenu;
    private bool _disposed;

    private ShellContextMenu(IContextMenu menu, IntPtr hmenu)
    {
        _menu = menu;
        _hmenu = hmenu;
    }

    /// <summary>
    /// Bouwt het menu voor een bestand, map of shell-item (zoals
    /// <c>::{645FF040-...}</c> voor de Prullenbak).
    /// </summary>
    /// <exception cref="COMException">Als de shell het item niet kent (of een <see cref="IOException"/> als het niet bestaat).</exception>
    public static ShellContextMenu Create(string path, bool extended = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, out _));
        try
        {
            var iidFolder = typeof(IShellFolder).GUID;
            Marshal.ThrowExceptionForHR(SHBindToParent(pidl, ref iidFolder, out var folderPtr, out var child));
            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);
            Marshal.Release(folderPtr);
            try
            {
                var iidMenu = typeof(IContextMenu).GUID;
                Marshal.ThrowExceptionForHR(folder.GetUIObjectOf(IntPtr.Zero, 1, [child], ref iidMenu, IntPtr.Zero, out var menuPtr));
                var menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);
                Marshal.Release(menuPtr);

                var hmenu = CreatePopupMenu();
                var flags = CMF_NORMAL | (extended ? CMF_EXTENDEDVERBS : 0);
                var hr = menu.QueryContextMenu(hmenu, 0, FirstCommand, LastCommand, flags);
                if (hr < 0)
                {
                    DestroyMenu(hmenu);
                    Marshal.ReleaseComObject(menu);
                    Marshal.ThrowExceptionForHR(hr);
                }
                return new ShellContextMenu(menu, hmenu);
            }
            finally
            {
                Marshal.ReleaseComObject(folder);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl); // child wijst hierin en hoeft niet apart vrij
        }
    }

    /// <summary>
    /// Toont het menu bij de muis en voert de gekozen optie uit. Geeft <c>false</c>
    /// als de shell geen menu voor dit item kan maken. Aanroepen op de UI-thread.
    /// </summary>
    public static bool TryShow(string path)
    {
        ShellContextMenu menu;
        try
        {
            menu = Create(path, extended: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            return false;
        }
        using (menu) menu.Show();
        return true;
    }

    /// <summary>De taalonafhankelijke namen van de opties in het menu (bijv. <c>open</c>, <c>delete</c>, <c>empty</c>).</summary>
    public IReadOnlyList<string> Verbs()
    {
        var verbs = new List<string>();
        var buffer = Marshal.AllocHGlobal(512 * sizeof(char));
        try
        {
            var count = GetMenuItemCount(_hmenu);
            for (var i = 0; i < count; i++)
            {
                var id = GetMenuItemID(_hmenu, i);
                if (id < FirstCommand || id > LastCommand) continue; // scheidingslijn of submenu
                Marshal.WriteInt16(buffer, 0);
                if (_menu.GetCommandString(id - FirstCommand, GCS_VERBW, IntPtr.Zero, buffer, 512) < 0) continue;
                if (Marshal.PtrToStringUni(buffer) is { Length: > 0 } verb) verbs.Add(verb);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return verbs;
    }

    private void Show()
    {
        GetCursorPos(out var pt);
        var owner = Helper();
        SetWindowPos(owner.Handle, IntPtr.Zero, pt.X, pt.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        _active = this;
        uint command;
        try
        {
            // Zonder voorgrondvenster sluit het menu niet als je ernaast klikt.
            SetForegroundWindow(owner.Handle);
            command = TrackPopupMenuEx(_hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, owner.Handle, IntPtr.Zero);
            PostMessage(owner.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            _active = null;
        }
        if (command >= FirstCommand) Invoke(command - FirstCommand, owner.Handle, pt);
    }

    private void Invoke(uint offset, IntPtr owner, POINT pt)
    {
        var modifiers = Keyboard.Modifiers;
        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
            fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE
                | (modifiers.HasFlag(ModifierKeys.Shift) ? CMIC_MASK_SHIFT_DOWN : 0)
                | (modifiers.HasFlag(ModifierKeys.Control) ? CMIC_MASK_CONTROL_DOWN : 0),
            hwnd = owner,
            lpVerb = (IntPtr)offset,
            lpVerbW = (IntPtr)offset,
            nShow = SW_SHOWNORMAL,
            ptInvoke = pt,
        };
        // Een fout (of "geannuleerd") meldt de shell zelf al; hier niets aan toe te voegen.
        _menu.InvokeCommand(ref info);
    }

    /// <summary>
    /// Onzichtbaar venster als eigenaar van menu en dialogen. Het blijft bestaan,
    /// want bijv. Eigenschappen kan nog open staan als het menu al weg is.
    /// </summary>
    private static HwndSource Helper()
    {
        if (_helper is not null) return _helper;
        _helper = new HwndSource(new HwndSourceParameters("DonkiePane.ShellMenu")
        {
            WindowStyle = unchecked((int)WS_POPUP),
            ExtendedWindowStyle = (int)WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0,
        });
        _helper.AddHook(ForwardMenuMessages);
        return _helper;
    }

    /// <summary>Submenu's als Openen met en Verzenden naar vullen zich pas via deze berichten.</summary>
    private static IntPtr ForwardMenuMessages(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_active is not { } active || msg is not (WM_INITMENUPOPUP or WM_DRAWITEM or WM_MEASUREITEM or WM_MENUCHAR))
            return IntPtr.Zero;

        if (active._menu is IContextMenu3 menu3)
        {
            if (menu3.HandleMenuMsg2((uint)msg, wParam, lParam, out var result) >= 0)
            {
                handled = true;
                return result;
            }
        }
        else if (active._menu is IContextMenu2 menu2 && msg != WM_MENUCHAR)
        {
            if (menu2.HandleMenuMsg((uint)msg, wParam, lParam) >= 0)
            {
                handled = true;
                return msg == WM_INITMENUPOPUP ? IntPtr.Zero : new IntPtr(1);
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyMenu(_hmenu);
        Marshal.ReleaseComObject(_menu);
    }

    // ---------- Win32 en COM ----------

    private const uint CMF_NORMAL = 0x0;
    private const uint CMF_EXTENDEDVERBS = 0x100;
    private const uint GCS_VERBW = 0x4;
    private const uint CMIC_MASK_UNICODE = 0x4000;
    private const uint CMIC_MASK_SHIFT_DOWN = 0x10000000;
    private const uint CMIC_MASK_PTINVOKE = 0x20000000;
    private const uint CMIC_MASK_CONTROL_DOWN = 0x40000000;
    private const int SW_SHOWNORMAL = 1;
    private const uint TPM_RIGHTBUTTON = 0x2;
    private const uint TPM_RETURNCMD = 0x100;
    private const int WM_NULL = 0x0000;
    private const int WM_DRAWITEM = 0x002B;
    private const int WM_MEASUREITEM = 0x002C;
    private const int WM_INITMENUPOPUP = 0x0117;
    private const int WM_MENUCHAR = 0x0120;
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x80;
    private const uint SWP_NOSIZE = 0x1;
    private const uint SWP_NOZORDER = 0x4;
    private const uint SWP_NOACTIVATE = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        // Alleen GetUIObjectOf wordt gebruikt; de rest staat er voor de juiste volgorde.
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, IntPtr name, IntPtr eaten, IntPtr pidl, IntPtr attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint flags, IntPtr enumIdList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwnd, ref Guid riid, IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, IntPtr apidl, IntPtr attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
            ref Guid riid, IntPtr reserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, IntPtr name, uint flags, IntPtr pidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(nuint idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(nuint idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(nuint idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr pbc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid riid, out IntPtr ppv, out IntPtr pidlLast);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hmenu);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr hmenu);

    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(IntPtr hmenu, int pos);

    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}

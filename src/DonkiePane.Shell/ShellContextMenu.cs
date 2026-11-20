using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using DonkiePane.Core;
using static DonkiePane.Shell.ShellNative;

namespace DonkiePane.Shell;

/// <summary>
/// Het rechtermuisknopmenu van Verkenner voor bureaubladitems: dezelfde
/// opties als op het bureaublad (met "Meer opties weergeven"), zoals
/// Prullenbak leegmaken, Openen met, Kopiëren, Verwijderen en Eigenschappen.
/// Met Shift erbij komen ook de uitgebreide opties (bijv. Kopiëren als pad).
/// Daarnaast het menu van een lege plek (Nieuw, Plakken) en het rechtstreeks
/// uitvoeren van een optie (Delete-toets = "delete").
///
/// "Naam wijzigen" voert de shell buiten Verkenner niet zelf uit; dat doet de
/// pane (zie <see cref="TryShow(IReadOnlyList{string}, Action?)"/>).
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

    private readonly IContextMenu? _menu; // null: een menu met alleen eigen opties
    private readonly IntPtr _hmenu;
    private bool _disposed;

    private ShellContextMenu(IContextMenu? menu, IntPtr hmenu)
    {
        _menu = menu;
        _hmenu = hmenu;
    }

    /// <summary>Het onzichtbare venster waar menu's en meldingen van de shell aan hangen.</summary>
    public static IntPtr OwnerHandle => Helper().Handle;

    /// <summary>
    /// Bouwt het menu voor een bestand, map of shell-item (zoals
    /// <c>::{645FF040-...}</c> voor de Prullenbak).
    /// </summary>
    /// <exception cref="COMException">Als de shell het item niet kent (of een <see cref="IOException"/> als het niet bestaat).</exception>
    public static ShellContextMenu Create(string path, bool extended = false) => Create([path], extended);

    /// <summary>Het menu voor meerdere items. Ze moeten in dezelfde map staan (zie <see cref="ShellNative.GroupByFolder"/>).</summary>
    public static ShellContextMenu Create(IReadOnlyList<string> paths, bool extended = false, bool canRename = false)
    {
        foreach (var path in paths) ArgumentException.ThrowIfNullOrEmpty(path);
        using var items = new ItemsInFolder(paths);
        var menu = items.GetUIObject<IContextMenu>(IntPtr.Zero, IID_IContextMenu)
                   ?? throw new COMException("Geen menu voor dit item.");
        return Query(menu, CMF_NORMAL | (extended ? CMF_EXTENDEDVERBS : 0) | (canRename ? CMF_CANRENAME : 0));
    }

    private static ShellContextMenu Query(IContextMenu menu, uint flags)
    {
        var hmenu = CreatePopupMenu();
        var hr = menu.QueryContextMenu(hmenu, 0, FirstCommand, LastCommand, flags);
        if (hr < 0)
        {
            DestroyMenu(hmenu);
            Marshal.ReleaseComObject(menu);
            Marshal.ThrowExceptionForHR(hr);
        }
        return new ShellContextMenu(menu, hmenu);
    }

    /// <summary>Het menu voor één item tonen (zie <see cref="TryShow(IReadOnlyList{string}, Action?)"/>).</summary>
    public static bool TryShow(string path) => TryShow([path]);

    /// <summary>
    /// Toont het menu bij de muis en voert de gekozen optie uit. Geeft <c>false</c>
    /// als de shell geen menu voor deze items kan maken. Aanroepen op de UI-thread.
    /// Staan de items in verschillende mappen, dan geldt het menu voor die van het eerste item.
    /// </summary>
    /// <param name="rename">Uitgevoerd bij "Naam wijzigen"; <c>null</c> = die optie niet tonen.</param>
    public static bool TryShow(IReadOnlyList<string> paths, Action? rename = null)
    {
        if (paths.Count == 0) return false;
        var group = GroupByFolder(paths).First(g => g.Contains(paths[0]));
        if (!TryCreate(() => Create(group, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), rename is not null), out var menu))
            return false;
        using (menu) menu.Show(rename);
        return true;
    }

    // Eigen opties in het menu van een lege plek; buiten het bereik van de shell-opdrachten.
    private const uint CmdPaste = 0x8001, CmdPasteLink = 0x8002, CmdDisplay = 0x8003, CmdPersonalize = 0x8004;

    /// <summary>
    /// Het menu van een lege plek, zoals op het bureaublad: Plakken, Snelkoppeling plakken, Nieuw (map, snelkoppeling,
    /// tekstdocument, ...), Beeldscherminstellingen en Persoonlijke instellingen. Nieuwe items komen in
    /// <paramref name="folderPath"/>. (De shell geeft buiten Verkenner voor een lege plek geen Plakken of Nieuw.)
    /// </summary>
    /// <param name="beforeCommand">Vlak voordat een optie die iets in de map zet wordt uitgevoerd (niet bij annuleren).</param>
    /// <param name="newItem">Na het maken van een nieuw item via Nieuw (de pane laat dan de naam wijzigen).</param>
    public static void ShowBackground(string folderPath, Action? beforeCommand = null, Action? newItem = null)
    {
        using var menu = CreateBackgroundMenu(folderPath);
        var command = menu.Track();
        switch (command)
        {
            case 0:
                return;
            case CmdPaste or CmdPasteLink:
                beforeCommand?.Invoke();
                ShellItems.Paste(folderPath, asLink: command == CmdPasteLink);
                return;
            case CmdDisplay:
                OpenSettings("ms-settings:display");
                return;
            case CmdPersonalize:
                OpenSettings("ms-settings:personalization");
                return;
            default:
                if (command < FirstCommand || command > LastCommand || menu._menu is null) return;
                beforeCommand?.Invoke();
                GetCursorPos(out var pt);
                if (menu.Invoke((IntPtr)(command - FirstCommand), Helper().Handle, pt, ModifierKeys.None) >= 0) newItem?.Invoke();
                return;
        }
    }

    /// <summary>Bouwt het menu van een lege plek (zie <see cref="ShowBackground"/>), zonder het te tonen.</summary>
    public static ShellContextMenu CreateBackgroundMenu(string folderPath)
    {
        var hmenu = CreatePopupMenu();
        var paste = ShellItems.CanPaste() ? MF_STRING : MF_STRING | MF_GRAYED;
        AppendMenu(hmenu, paste, CmdPaste, Loc.T("Plakken"));
        AppendMenu(hmenu, paste, CmdPasteLink, Loc.T("Snelkoppeling plakken"));
        AppendMenu(hmenu, MF_SEPARATOR, 0, null);

        // Het Nieuw-menu van Windows zelf: alle soorten die op deze pc geregistreerd zijn.
        IContextMenu? newMenu = null;
        try
        {
            newMenu = Create<IContextMenu>(CLSID_NewMenu);
            if (newMenu is IShellExtInit init && SHParseDisplayName(folderPath, IntPtr.Zero, out var pidl, 0, out _) >= 0)
            {
                try
                {
                    if (init.Initialize(pidl, IntPtr.Zero, IntPtr.Zero) < 0 ||
                        newMenu.QueryContextMenu(hmenu, (uint)GetMenuItemCount(hmenu), FirstCommand, LastCommand, CMF_NORMAL) < 0)
                    {
                        Marshal.ReleaseComObject(newMenu);
                        newMenu = null;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            else if (newMenu is not null)
            {
                Marshal.ReleaseComObject(newMenu);
                newMenu = null;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            newMenu = null;
        }

        if (newMenu is not null) AppendMenu(hmenu, MF_SEPARATOR, 0, null);
        AppendMenu(hmenu, MF_STRING, CmdDisplay, Loc.T("Beeldscherminstellingen"));
        AppendMenu(hmenu, MF_STRING, CmdPersonalize, Loc.T("Persoonlijke instellingen"));
        return new ShellContextMenu(newMenu, hmenu);
    }

    /// <summary>De teksten van de opties in dit menu (eerste niveau), bijv. om te testen.</summary>
    public IReadOnlyList<string> Labels()
    {
        var labels = new List<string>();
        var buffer = new System.Text.StringBuilder(256);
        var count = GetMenuItemCount(_hmenu);
        for (var i = 0; i < count; i++)
        {
            buffer.Clear();
            if (GetMenuString(_hmenu, (uint)i, buffer, buffer.Capacity, MF_BYPOSITION) > 0) labels.Add(buffer.ToString());
        }
        return labels;
    }

    private static void OpenSettings(string uri)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception) { }
    }

    /// <summary>
    /// Voert een optie uit zonder menu, zoals de Delete-toets ("delete") of Alt+Enter ("properties"). Items in
    /// verschillende mappen gaan per map.
    /// </summary>
    /// <param name="shift">Alsof Shift is ingedrukt (bijv. Shift+Delete: definitief verwijderen).</param>
    /// <returns><c>true</c> als de shell de optie voor minstens één groep heeft uitgevoerd.</returns>
    public static bool InvokeVerb(IReadOnlyList<string> paths, string verb, bool shift = false)
    {
        var done = false;
        foreach (var group in GroupByFolder(paths))
        {
            if (!TryCreate(() => Create(group), out var menu)) continue;
            using (menu) done |= menu.Invoke(verb, shift);
        }
        return done;
    }

    private static bool TryCreate(Func<ShellContextMenu> create, out ShellContextMenu menu)
    {
        try
        {
            menu = create();
            return true;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            menu = null!;
            return false;
        }
    }

    /// <summary>De taalonafhankelijke namen van de opties in het menu (bijv. <c>open</c>, <c>delete</c>, <c>empty</c>).</summary>
    public IReadOnlyList<string> Verbs()
    {
        var verbs = new List<string>();
        var count = GetMenuItemCount(_hmenu);
        for (var i = 0; i < count; i++)
        {
            var id = GetMenuItemID(_hmenu, i);
            if (id < FirstCommand || id > LastCommand) continue; // scheidingslijn of submenu
            if (VerbOf(id - FirstCommand) is { Length: > 0 } verb) verbs.Add(verb);
        }
        return verbs;
    }

    private string? VerbOf(uint offset)
    {
        if (_menu is null) return null;
        var buffer = Marshal.AllocHGlobal(512 * sizeof(char));
        try
        {
            Marshal.WriteInt16(buffer, 0);
            if (_menu.GetCommandString(offset, GCS_VERBW, IntPtr.Zero, buffer, 512) < 0) return null;
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Toont het menu bij de muis en geeft de gekozen opdracht (0 = niets gekozen).</summary>
    private uint Track()
    {
        GetCursorPos(out var pt);
        var owner = Helper();
        SetWindowPos(owner.Handle, IntPtr.Zero, pt.X, pt.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        _active = this;
        try
        {
            // Zonder voorgrondvenster sluit het menu niet als je ernaast klikt.
            SetForegroundWindow(owner.Handle);
            var command = TrackPopupMenuEx(_hmenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, owner.Handle, IntPtr.Zero);
            PostMessage(owner.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return command;
        }
        finally
        {
            _active = null;
        }
    }

    private void Show(Action? rename, Action? beforeCommand = null)
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
        if (command < FirstCommand) return;

        // De shell wijzigt buiten Verkenner zelf geen namen; dat doet de pane.
        if (rename is not null && string.Equals(VerbOf(command - FirstCommand), "rename", StringComparison.OrdinalIgnoreCase))
        {
            rename();
            return;
        }
        beforeCommand?.Invoke();
        Invoke((IntPtr)(command - FirstCommand), owner.Handle, pt, Keyboard.Modifiers);
    }

    private bool Invoke(string verb, bool shift)
    {
        GetCursorPos(out var pt);
        var verbPtr = Marshal.StringToHGlobalAnsi(verb);
        var verbPtrW = Marshal.StringToHGlobalUni(verb);
        try
        {
            var modifiers = shift ? ModifierKeys.Shift : ModifierKeys.None;
            return Invoke(verbPtr, Helper().Handle, pt, modifiers, verbPtrW) >= 0;
        }
        finally
        {
            Marshal.FreeHGlobal(verbPtr);
            Marshal.FreeHGlobal(verbPtrW);
        }
    }

    private int Invoke(IntPtr verb, IntPtr owner, POINT pt, ModifierKeys modifiers, IntPtr verbW = default)
    {
        if (_menu is null) return -1;
        var info = new CMINVOKECOMMANDINFOEX
        {
            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
            fMask = CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE
                | (modifiers.HasFlag(ModifierKeys.Shift) ? CMIC_MASK_SHIFT_DOWN : 0)
                | (modifiers.HasFlag(ModifierKeys.Control) ? CMIC_MASK_CONTROL_DOWN : 0),
            hwnd = owner,
            lpVerb = verb,
            lpVerbW = verbW == IntPtr.Zero ? verb : verbW,
            nShow = SW_SHOWNORMAL,
            ptInvoke = pt,
        };
        // Een fout (of "geannuleerd") meldt de shell zelf al; hier niets aan toe te voegen.
        return _menu.InvokeCommand(ref info);
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

    /// <summary>Submenu's als Openen met, Nieuw en Verzenden naar vullen zich pas via deze berichten.</summary>
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
        if (_menu is not null) Marshal.ReleaseComObject(_menu);
    }

    // ---------- Win32 en COM ----------

    private const uint CMF_NORMAL = 0x0;
    private const uint MF_STRING = 0x0, MF_GRAYED = 0x1, MF_SEPARATOR = 0x800, MF_BYPOSITION = 0x400;
    private const uint CMF_CANRENAME = 0x10;
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

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr hmenu, uint flags, nuint id, string? text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuString(IntPtr hmenu, uint item, System.Text.StringBuilder text, int max, uint flags);

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
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}

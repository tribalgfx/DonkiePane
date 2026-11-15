using System.Runtime.InteropServices;
using System.Text;
using DonkiePane.Core;

namespace DonkiePane.Shell;

/// <summary>
/// Meldt een dubbelklik op een leeg stuk bureaublad (niet op een pane, een venster of een icoon).
///
/// Windows geeft een programma geen klikken op het bureaublad van Verkenner, dus dit luistert via een
/// muishaak (WH_MOUSE_LL). De haak kijkt alleen mee, houdt niets tegen en leest geen toetsen. Hij moet
/// op een thread met berichtenlus worden aangemaakt (de UI-thread) en doet in de haak zelf bijna niets;
/// de melding komt asynchroon op die thread terecht.
/// </summary>
public sealed class DesktopDoubleClickWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const uint GA_ROOT = 2;
    private const uint LVM_GETSELECTEDCOUNT = 0x1032;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    private readonly HookProc _proc; // vasthouden: de garbage collector mag hem niet opruimen
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly DoubleClickDetector _detector = new(
        (int)GetDoubleClickTime(), Math.Max(GetSystemMetrics(36) / 2, 2), Math.Max(GetSystemMetrics(37) / 2, 2));
    private IntPtr _hook;

    public event EventHandler? DoubleClickedOnDesktop;

    public DesktopDoubleClickWatcher()
    {
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    /// <summary><c>false</c> als Windows de haak niet wilde plaatsen; dan werkt deze functie niet.</summary>
    public bool IsActive => _hook != IntPtr.Zero;

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (int)wParam == WM_LBUTTONDOWN)
        {
            try
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (IsEmptyDesktop(info.pt))
                {
                    if (_detector.Register(info.time, info.pt.X, info.pt.Y))
                        _context?.Post(_ => DoubleClickedOnDesktop?.Invoke(this, EventArgs.Empty), null);
                }
                else
                {
                    _detector.Reset();
                }
            }
            catch
            {
                // Een haak mag nooit een uitzondering teruggeven aan Windows.
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>Ligt dit punt op het bureaublad zelf, en niet op een icoon?</summary>
    private static bool IsEmptyDesktop(POINT point)
    {
        var hwnd = WindowFromPoint(point);
        if (hwnd == IntPtr.Zero) return false;

        var root = GetAncestor(hwnd, GA_ROOT);
        var rootClass = ClassName(root);
        if (rootClass is not ("Progman" or "WorkerW")) return false;

        // Zijn de Windows-iconen zichtbaar, dan kan dit punt op een icoon liggen. Na de eerste klik van
        // een dubbelklik op een leeg stuk is er niets geselecteerd; op een icoon wel.
        if (ClassName(hwnd) == "SysListView32" && IsWindowVisible(hwnd))
        {
            if (SendMessageTimeout(hwnd, LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 100, out var selected) == IntPtr.Zero)
                return false;
            return selected == IntPtr.Zero;
        }
        return true;
    }

    private static string ClassName(IntPtr hwnd)
    {
        var text = new StringBuilder(64);
        return GetClassName(hwnd, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);
}

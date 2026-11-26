using System.Windows.Interop;
using static DonkiePane.Shell.NativeMethods;

namespace DonkiePane.Shell;

/// <summary>
/// Sneltoetsen die overal werken, ook als DonkiePane niet het actieve venster is (RegisterHotKey). Een sneltoets die al
/// door Windows of een ander programma gebruikt wordt, kan niet: <see cref="Register"/> geeft dan <c>false</c>.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly HashSet<int> _registered = [];

    /// <summary>Een sneltoets is ingedrukt (het nummer van <see cref="Register"/>). Komt binnen op de UI-thread.</summary>
    public event EventHandler<int>? Pressed;

    public GlobalHotkeys()
    {
        // Een onzichtbaar venster alleen voor berichten.
        _source = new HwndSource(new HwndSourceParameters("DonkiePane.Hotkeys") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    /// <param name="modifiers">Alt = 1, Ctrl = 2, Shift = 4, Win = 8 (zoals RegisterHotKey).</param>
    /// <param name="virtualKey">De virtuele toetscode.</param>
    public bool Register(int id, int modifiers, int virtualKey)
    {
        Unregister(id);
        if (virtualKey == 0 || !RegisterHotKey(_source.Handle, id, (uint)modifiers | MOD_NOREPEAT, (uint)virtualKey)) return false;
        _registered.Add(id);
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id)) UnregisterHotKey(_source.Handle, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            Pressed?.Invoke(this, (int)wParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _registered.ToList()) Unregister(id);
        _source.Dispose();
    }
}

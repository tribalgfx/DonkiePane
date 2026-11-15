using System.Windows.Interop;
using static DonkiePane.Shell.NativeMethods;

namespace DonkiePane.Shell;

/// <summary>
/// Meldt wanneer Verkenner (explorer.exe) opnieuw is gestart. Het oude
/// bureaubladvenster bestaat dan niet meer, en panes die daaraan gekoppeld
/// waren zijn door Windows gesloten; ze moeten opnieuw worden opgebouwd.
///
/// Windows stuurt dan het bericht "TaskbarCreated" naar alle
/// topniveauvensters, dus luisteren we met een onzichtbaar venster.
/// </summary>
public sealed class ShellRestartWatcher : IDisposable
{
    private readonly HwndSource _source;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

    public event EventHandler? ShellRestarted;

    public ShellRestartWatcher()
    {
        // Een onzichtbaar topniveauvenster (geen message-only venster: die
        // ontvangen geen broadcasts).
        _source = new HwndSource(new HwndSourceParameters("DonkiePane.ShellWatcher")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = (int)WS_EX_TOOLWINDOW,
        });
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_taskbarCreated != 0 && msg == (int)_taskbarCreated)
            ShellRestarted?.Invoke(this, EventArgs.Empty);
        return IntPtr.Zero;
    }

    public void Dispose() => _source.Dispose();
}

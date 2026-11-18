using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using static DonkiePane.Shell.NativeMethods;

namespace DonkiePane.Shell;

/// <summary>
/// Meldt wanneer de Prullenbak verandert (iets erin gegooid, leeggemaakt,
/// teruggezet), zodat een pane het icoon kan wisselen tussen vol en leeg.
/// Het icoon zelf verandert niet vanzelf: DonkiePane maakt er één keer een
/// afbeelding van.
///
/// Werkt met de wijzigingsmeldingen van de shell (dezelfde waarmee Verkenner
/// zijn eigen Prullenbakicoon bijwerkt), via een onzichtbaar venster. Meerdere
/// meldingen kort na elkaar (bijv. honderd bestanden weggooien) worden één
/// <see cref="Changed"/>.
/// </summary>
public sealed class RecycleBinWatcher : IDisposable
{
    private const string RecycleBin = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private const int WM_SHELLNOTIFY = 0x0400 + 0x2F1; // WM_USER + iets eigens

    private readonly HwndSource _source;
    private readonly DispatcherTimer _debounce;
    private readonly uint _registration;

    public event EventHandler? Changed;

    public RecycleBinWatcher(TimeSpan? debounce = null)
    {
        _debounce = new DispatcherTimer { Interval = debounce ?? TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        _source = new HwndSource(new HwndSourceParameters("DonkiePane.RecycleBinWatcher")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, onzichtbaar
            ExtendedWindowStyle = (int)WS_EX_TOOLWINDOW,
        });
        _source.AddHook(WndProc);

        if (SHParseDisplayName(RecycleBin, IntPtr.Zero, out var pidl, 0, out _) < 0) return;
        try
        {
            var entries = new[] { new SHChangeNotifyEntry { pidl = pidl, fRecursive = true } };
            _registration = SHChangeNotifyRegister(_source.Handle, SHCNRF_InterruptLevel | SHCNRF_ShellLevel,
                SHCNE_ALLEVENTS, WM_SHELLNOTIFY, entries.Length, entries);
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl); // de shell maakt bij registreren een eigen kopie
        }
    }

    /// <summary>Of de shell de registratie heeft geaccepteerd.</summary>
    public bool IsWatching => _registration != 0;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_SHELLNOTIFY) return IntPtr.Zero;
        handled = true;
        _debounce.Stop();
        _debounce.Start();
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _debounce.Stop();
        if (_registration != 0) SHChangeNotifyDeregister(_registration);
        _source.Dispose();
    }

    private const int SHCNRF_InterruptLevel = 0x0001;
    private const int SHCNRF_ShellLevel = 0x0002;
    private const int SHCNE_ALLEVENTS = 0x7FFFFFFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct SHChangeNotifyEntry
    {
        public IntPtr pidl;
        [MarshalAs(UnmanagedType.Bool)] public bool fRecursive;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr pbc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern uint SHChangeNotifyRegister(IntPtr hwnd, int sources, int events, int msg, int count,
        [In] SHChangeNotifyEntry[] entries);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHChangeNotifyDeregister(uint id);
}

using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DonkiePane.Shell;
using Microsoft.VisualBasic.FileIO;

namespace DonkiePane.App.Tests;

public sealed class RecycleBinWatcherTests
{
    /// <summary>
    /// Gooit een eigen tijdelijk bestand in de echte Prullenbak en haalt het er
    /// daarna weer uit. Daarom alleen met DONKIEPANE_RECYCLEBIN_TEST=1.
    /// </summary>
    [Fact]
    public void Throwing_something_away_and_taking_it_out_again_are_both_noticed()
    {
        if (Environment.GetEnvironmentVariable("DONKIEPANE_RECYCLEBIN_TEST") != "1") return;

        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var watcher = new RecycleBinWatcher(TimeSpan.FromMilliseconds(100));
                Assert.True(watcher.IsWatching);
                var changes = 0;
                watcher.Changed += (_, _) => changes++;

                var wasEmpty = BinIsEmpty();
                var before = IconPixels();

                var name = $"donkiepane-prullenbaktest-{Guid.NewGuid():N}.txt";
                var file = Path.Combine(Path.GetTempPath(), name);
                File.WriteAllText(file, "mag weg");
                FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

                Assert.True(PumpUntil(() => changes > 0, TimeSpan.FromSeconds(5)), "weggooien niet gemeld");
                var afterThrowing = IconPixels();

                changes = 0;
                Assert.True(RemoveFromRecycleBin(name), "testbestand niet teruggevonden in de Prullenbak");
                Assert.True(PumpUntil(() => changes > 0, TimeSpan.FromSeconds(5)), "eruit halen niet gemeld");
                var afterRemoving = IconPixels();

                // Stond de Prullenbak leeg, dan hoort het icoon "vol" te worden en daarna weer "leeg".
                if (wasEmpty)
                {
                    Assert.NotEqual(before, afterThrowing);
                    Assert.Equal(before, afterRemoving);
                }
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("Fout op de UI-thread: " + error, error);
    }

    [Fact]
    public void The_shell_accepts_the_registration()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var watcher = new RecycleBinWatcher();
                Assert.True(watcher.IsWatching);
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("Fout op de UI-thread: " + error, error);
    }

    private static byte[] IconPixels()
    {
        var icon = ShellItemInfo.GetIcon(VirtualDesktopItems.ParsingName("{645FF040-5081-101B-9F08-00AA002F954E}"), 48)!;
        var pixels = new byte[icon.PixelWidth * icon.PixelHeight * 4];
        icon.CopyPixels(pixels, icon.PixelWidth * 4, 0);
        return pixels;
    }

    /// <summary>Alleen dan valt het wisselen van het icoon te controleren (anders blijft hij "vol").</summary>
    private static bool BinIsEmpty()
    {
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var bin = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "$Recycle.Bin", sid);
        return !Directory.EnumerateFiles(bin, "$I*").Any();
    }

    private static bool PumpUntil(Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!done() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        return done();
    }

    /// <summary>
    /// Alleen ons eigen testbestand: per weggegooid item staat er een $I-bestand
    /// (met het oude pad) en een $R-bestand (de inhoud) met dezelfde code.
    /// </summary>
    private static bool RemoveFromRecycleBin(string name)
    {
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var bin = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "$Recycle.Bin", sid);
        foreach (var info in Directory.EnumerateFiles(bin, "$I*"))
        {
            if (!File.ReadAllText(info, System.Text.Encoding.Unicode).Contains(name)) continue;
            var data = Path.Combine(bin, "$R" + Path.GetFileName(info)[2..]);
            File.Delete(data);
            File.Delete(info);
            SHUpdateRecycleBinIcon();
            return true;
        }
        return false;
    }

    [DllImport("shell32.dll")]
    private static extern void SHUpdateRecycleBinIcon();
}

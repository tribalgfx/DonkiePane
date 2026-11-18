using System.IO;
using System.Runtime.InteropServices;
using DonkiePane.Shell;

namespace DonkiePane.App.Tests;

/// <summary>Bouwt het Verkenner-menu zonder het te tonen of een optie uit te voeren.</summary>
public sealed class ShellContextMenuTests
{
    private static IReadOnlyList<string> VerbsOf(string path, bool extended = false)
    {
        IReadOnlyList<string>? verbs = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var menu = ShellContextMenu.Create(path, extended);
                verbs = menu.Verbs();
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("Fout op de STA-thread: " + error, error);
        return verbs!;
    }

    [Fact]
    public void A_file_gets_the_explorer_options_but_no_rename()
    {
        var file = Path.Combine(Path.GetTempPath(), $"donkiepane-menu-{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, "test");
        try
        {
            var verbs = VerbsOf(file);

            Assert.Contains("open", verbs);
            Assert.Contains("delete", verbs);
            Assert.Contains("properties", verbs);
            Assert.DoesNotContain("rename", verbs); // werkt alleen in een Verkenner-venster
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void The_recycle_bin_can_be_emptied_from_its_menu()
    {
        var verbs = VerbsOf(VirtualDesktopItems.ParsingName("{645FF040-5081-101B-9F08-00AA002F954E}"));

        Assert.Contains("empty", verbs);
    }

    [Fact]
    public void A_vanished_item_shows_no_menu_so_the_pane_can_fall_back_to_its_own()
    {
        bool? shown = null;
        var thread = new Thread(() => shown = ShellContextMenu.TryShow(@"C:\bestaat\niet\echt-" + Guid.NewGuid().ToString("N") + ".txt"));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.False(shown);
    }
}

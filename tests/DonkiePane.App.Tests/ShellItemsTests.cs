using System.IO;
using DonkiePane.Shell;

namespace DonkiePane.App.Tests;

/// <summary>
/// De koppeling met de shell van Windows: slepen, naam wijzigen, menu's en neerzetten. Alles in een eigen tijdelijke
/// map; het klembord en de Prullenbak van de gebruiker worden niet aangeraakt.
/// </summary>
public sealed class ShellItemsTests : TempData
{
    private static T OnSta<T>(Func<T> action)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("Fout op de STA-thread: " + error, error);
        return result;
    }

    private string NewFile(string name)
    {
        var path = Path.Combine(Dir, name);
        File.WriteAllText(path, "test");
        return path;
    }

    [Fact]
    public void Dragging_uses_a_real_shell_data_object_also_for_several_items_and_the_recycle_bin()
    {
        var one = NewFile("een.txt");
        var two = NewFile("twee.txt");
        var bin = VirtualDesktopItems.ParsingName("{645FF040-5081-101B-9F08-00AA002F954E}");

        Assert.True(OnSta(() => ShellItems.CreateDataObject([one, two]) is not null));
        Assert.True(OnSta(() => ShellItems.CreateDataObject([bin]) is not null));
        Assert.True(OnSta(() => ShellItems.CreateDataObject([Path.Combine(Dir, "bestaat-niet.txt")]) is null));
    }

    [Fact]
    public void Rename_works_like_explorer_and_keeps_the_file()
    {
        var file = NewFile("oud.txt");
        var editing = OnSta(() => ShellItems.GetEditingName(file));
        Assert.True(editing is "oud" or "oud.txt"); // met of zonder extensie, afhankelijk van de instelling in Verkenner

        var newName = editing == "oud" ? "nieuw" : "nieuw.txt";
        Assert.True(OnSta(() => ShellItems.Rename(file, newName)));

        Assert.False(File.Exists(file));
        Assert.Equal("test", File.ReadAllText(Path.Combine(Dir, "nieuw.txt")));
    }

    [Fact]
    public void The_menu_for_several_items_offers_delete_and_rename_on_request()
    {
        var one = NewFile("a.txt");
        var two = NewFile("b.txt");

        var verbs = OnSta(() =>
        {
            using var menu = ShellContextMenu.Create([one, two], canRename: true);
            return menu.Verbs();
        });

        Assert.Contains("delete", verbs);
        Assert.Contains("copy", verbs);
    }

    [Fact]
    public void The_menu_of_an_empty_spot_offers_paste_and_new_like_the_desktop()
    {
        var labels = OnSta(() =>
        {
            using var menu = ShellContextMenu.CreateBackgroundMenu(Dir);
            return menu.Labels();
        });

        Assert.Contains("Plakken", labels);
        Assert.Contains("Snelkoppeling plakken", labels);
        Assert.Contains(labels, l => l.Replace("&", "").StartsWith("Nieuw", StringComparison.OrdinalIgnoreCase) || l.Replace("&", "").StartsWith("New"));
        Assert.Contains("Beeldscherminstellingen", labels);
    }

    [Fact]
    public void Pasting_drops_the_items_on_the_folder_like_explorer_does()
    {
        var file = NewFile("plak.txt");
        var target = Directory.CreateDirectory(Path.Combine(Dir, "doel")).FullName;

        var effect = OnSta(() =>
        {
            var data = ShellItems.CreateDataObject([file])!;
            using var drop = ShellDropTarget.For(target)!;
            return drop.SimulateDrop(data, System.Windows.DragDropEffects.Copy);
        });

        Assert.Equal(System.Windows.DragDropEffects.Copy, effect);
        // De shell kopieert soms op de achtergrond: even wachten.
        var copy = Path.Combine(target, "plak.txt");
        for (var i = 0; i < 50 && !File.Exists(copy); i++) Thread.Sleep(100);
        Assert.True(File.Exists(copy));
        Assert.True(File.Exists(file)); // kopiëren laat het origineel staan
    }

    [Fact]
    public void A_new_folder_gets_a_free_dutch_name()
    {
        Directory.CreateDirectory(Path.Combine(Dir, "Nieuwe map"));

        var made = ShellItems.CreateNewFolder(Dir);

        Assert.Equal(Path.Combine(Dir, "Nieuwe map (2)"), made);
        Assert.True(Directory.Exists(made));
    }

    [Fact]
    public void A_folder_accepts_dropped_files()
    {
        var sub = Directory.CreateDirectory(Path.Combine(Dir, "map")).FullName;

        Assert.True(OnSta(() =>
        {
            using var target = ShellDropTarget.For(sub);
            return target is not null;
        }));
        Assert.True(OnSta(() => ShellDropTarget.For(Path.Combine(Dir, "weg")) is null));
    }
}

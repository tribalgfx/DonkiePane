using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class LayoutEditorTests
{
    private static Layout LayoutWith(params Pane[] panes) => new() { Panes = [.. panes] };

    [Fact]
    public void Sync_creates_inbox_and_puts_new_items_in_it()
    {
        var layout = new Layout();

        var changed = LayoutEditor.Sync(layout, ["user/b.txt", "user/a.lnk"]);

        Assert.True(changed);
        var inbox = Assert.Single(layout.Panes);
        Assert.True(inbox.IsInbox);
        Assert.Equal(["user/a.lnk", "user/b.txt"], inbox.Items);
    }

    [Fact]
    public void Sync_keeps_items_where_they_are_and_removes_vanished_ones()
    {
        var work = new Pane { Items = ["user/werk.lnk", "user/weg.txt"] };
        var inbox = new Pane { IsInbox = true };
        var layout = LayoutWith(work, inbox);

        LayoutEditor.Sync(layout, ["user/werk.lnk", "public/nieuw.lnk"]);

        Assert.Equal(["user/werk.lnk"], work.Items);
        Assert.Equal(["public/nieuw.lnk"], inbox.Items);
    }

    [Fact]
    public void Repair_makes_a_damaged_layout_usable()
    {
        var tasks = new Pane { Kind = PaneKind.Tasks, IsInbox = true, Width = -50, Height = double.NaN, Items = null! };
        var layout = new Layout { Panes = [tasks, null!] };

        Assert.True(LayoutEditor.Repair(layout));

        var pane = Assert.Single(layout.Panes);
        Assert.False(pane.IsInbox); // een takenpane toont geen bureaubladitems
        Assert.Equal(LayoutEditor.MinPaneWidth, pane.Width);
        Assert.Equal(220, pane.Height);
        Assert.Empty(pane.Items);
        Assert.False(LayoutEditor.Repair(layout)); // daarna is er niets meer te herstellen
    }

    [Fact]
    public void A_tasks_pane_marked_as_inbox_is_never_merged_away()
    {
        var tasks = new Pane { Kind = PaneKind.Tasks, IsInbox = true };
        var layout = LayoutWith(new Pane { IsInbox = true }, tasks);

        LayoutEditor.Sync(layout, ["user/a.txt"]);

        Assert.Contains(tasks, layout.Panes);
    }

    [Fact]
    public void Sync_keeps_the_items_of_a_desktop_folder_that_is_briefly_unreachable()
    {
        // Bijv. een OneDrive- of netwerkbureaublad dat bij het inloggen nog niet verbonden is.
        var work = new Pane { Items = ["user/werk.lnk", "public/firefox.lnk", "public/weg.lnk"] };
        var inbox = new Pane { IsInbox = true };
        var layout = LayoutWith(work, inbox);

        var changed = LayoutEditor.Sync(layout, ["public/firefox.lnk"], unavailableLocations: ["user"]);

        Assert.True(changed);
        Assert.Equal(["user/werk.lnk", "public/firefox.lnk"], work.Items); // alleen wat in een leesbare map echt weg is
        Assert.Empty(inbox.Items);
    }

    [Fact]
    public void Sync_reports_no_change_when_layout_already_matches()
    {
        var layout = LayoutWith(new Pane { IsInbox = true, Items = ["user/a.txt"] });

        Assert.False(LayoutEditor.Sync(layout, ["user/a.txt"]));
    }

    [Fact]
    public void Sync_removes_duplicates_keeping_first_occurrence()
    {
        var first = new Pane { Items = ["user/a.txt"] };
        var second = new Pane { IsInbox = true, Items = ["user/a.txt"] };
        var layout = LayoutWith(first, second);

        LayoutEditor.Sync(layout, ["user/a.txt"]);

        Assert.Equal(["user/a.txt"], first.Items);
        Assert.Empty(second.Items);
    }

    [Fact]
    public void Sync_ignores_case_differences()
    {
        var pane = new Pane { Items = ["user/Notities.TXT"] };
        var layout = LayoutWith(pane, new Pane { IsInbox = true });

        LayoutEditor.Sync(layout, ["user/notities.txt"]);

        Assert.Equal(["user/Notities.TXT"], pane.Items);
    }

    [Fact]
    public void Rename_keeps_position()
    {
        var pane = new Pane { Items = ["user/a.txt", "user/oud.txt", "user/c.txt"] };
        var layout = LayoutWith(pane);

        Assert.True(LayoutEditor.Rename(layout, "user/oud.txt", "user/nieuw.txt"));

        Assert.Equal(["user/a.txt", "user/nieuw.txt", "user/c.txt"], pane.Items);
    }

    [Fact]
    public void MoveItem_to_other_pane_at_index()
    {
        var from = new Pane { Items = ["user/a", "user/b"] };
        var to = new Pane { Items = ["user/x", "user/y"] };
        var layout = LayoutWith(from, to);

        LayoutEditor.MoveItem(layout, "user/a", to.Id, 1);

        Assert.Equal(["user/b"], from.Items);
        Assert.Equal(["user/x", "user/a", "user/y"], to.Items);
    }

    [Theory]
    [InlineData(0, 3, new[] { "b", "c", "a" })]   // naar achteren: vóór "einde"
    [InlineData(0, 2, new[] { "b", "a", "c" })]   // vóór "c"
    [InlineData(2, 0, new[] { "c", "a", "b" })]   // naar voren
    [InlineData(1, -1, new[] { "a", "c", "b" })]  // achteraan
    public void MoveItem_within_same_pane(int from, int to, string[] expected)
    {
        var pane = new Pane { Items = ["a", "b", "c"] };
        var layout = LayoutWith(pane);

        LayoutEditor.MoveItem(layout, pane.Items[from], pane.Id, to);

        Assert.Equal(expected, pane.Items);
    }

    [Fact]
    public void MoveItem_into_task_pane_is_refused()
    {
        var icons = new Pane { Items = ["user/a"] };
        var tasks = Pane.NewTaskPane();
        var layout = LayoutWith(icons, tasks);

        Assert.False(LayoutEditor.MoveItem(layout, "user/a", tasks.Id));
        Assert.Equal(["user/a"], icons.Items);
        Assert.Empty(tasks.Items);
    }

    [Fact]
    public void DeletePane_moves_items_to_inbox()
    {
        var pane = new Pane { Items = ["user/a"] };
        var inbox = new Pane { IsInbox = true, Items = ["user/z"] };
        var layout = LayoutWith(pane, inbox);

        Assert.True(LayoutEditor.DeletePane(layout, pane.Id));

        Assert.Equal([inbox], layout.Panes);
        Assert.Equal(["user/z", "user/a"], inbox.Items);
    }

    [Fact]
    public void Inbox_cannot_be_deleted()
    {
        var inbox = new Pane { IsInbox = true };
        var layout = LayoutWith(inbox);

        Assert.False(LayoutEditor.DeletePane(layout, inbox.Id));
        Assert.Single(layout.Panes);
    }

    [Fact]
    public void Old_layout_without_items_still_loads()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "layout.json");
            File.WriteAllText(file, """{ "Version": 1, "Panes": [ { "Title": "Oud", "Left": 1, "Top": 2 } ] }""");

            var layout = new LayoutStore(file).Load();

            var pane = Assert.Single(layout.Panes);
            Assert.Empty(pane.Items);
            Assert.False(pane.IsInbox);
            Assert.True(layout.HideDesktopIcons);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Sync_puts_new_items_in_the_pane_where_they_were_pasted()
    {
        var work = new Pane { Items = ["user/werk.lnk"] };
        var inbox = new Pane { IsInbox = true };
        var layout = LayoutWith(work, inbox);

        LayoutEditor.Sync(layout, ["user/werk.lnk", "user/geplakt.txt"], newItemsPane: work.Id);

        Assert.Equal(["user/werk.lnk", "user/geplakt.txt"], work.Items);
        Assert.Empty(inbox.Items);
    }

    [Fact]
    public void Sync_uses_the_inbox_when_the_paste_target_is_gone_or_a_tasks_pane()
    {
        var tasks = new Pane { Kind = PaneKind.Tasks };
        var inbox = new Pane { IsInbox = true };
        var layout = LayoutWith(tasks, inbox);

        LayoutEditor.Sync(layout, ["user/a.txt"], newItemsPane: tasks.Id);
        LayoutEditor.Sync(layout, ["user/a.txt", "user/b.txt"], newItemsPane: Guid.NewGuid());

        Assert.Equal(["user/a.txt", "user/b.txt"], inbox.Items);
    }

    [Theory]
    [InlineData(-1, new[] { "c", "e", "a", "d" })] // achteraan, in de volgorde van de selectie
    [InlineData(0, new[] { "a", "d", "c", "e" })]  // vóór c
    [InlineData(3, new[] { "c", "a", "d", "e" })]  // vóór e (d zelf telt niet als plek)
    [InlineData(2, new[] { "c", "a", "d", "e" })]  // op d zelf: vóór het eerstvolgende andere item
    public void MoveItems_keeps_the_selection_together_in_order(int index, string[] expected)
    {
        var pane = new Pane { Items = ["a", "c", "d", "e"] };
        var layout = LayoutWith(pane, new Pane { IsInbox = true });

        Assert.True(LayoutEditor.MoveItems(layout, ["a", "d"], pane.Id, index));

        Assert.Equal(expected, pane.Items);
    }

    [Fact]
    public void MoveItems_from_two_panes_into_a_third()
    {
        var one = new Pane { Items = ["a", "b"] };
        var two = new Pane { Items = ["c"] };
        var target = new Pane { Items = ["x"] };
        var layout = LayoutWith(one, two, target, new Pane { IsInbox = true });

        LayoutEditor.MoveItems(layout, ["b", "c"], target.Id, 0);

        Assert.Equal(["b", "c", "x"], target.Items);
        Assert.Equal(["a"], one.Items);
        Assert.Empty(two.Items);
    }
}

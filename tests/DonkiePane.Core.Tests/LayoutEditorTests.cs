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
}

using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class RulesTests
{
    [Theory]
    [InlineData(RuleKind.Extension, "pdf, .DOCX", "rapport.docx", false, true)]
    [InlineData(RuleKind.Extension, "pdf", "rapport.docx", false, false)]
    [InlineData(RuleKind.Extension, "pdf", "pdf", true, false)]          // een map heet misschien zo, maar is geen pdf
    [InlineData(RuleKind.NameContains, "factuur", "Factuur 2026.pdf", false, true)]
    [InlineData(RuleKind.NameContains, "", "iets.txt", false, false)]    // lege tekst past nergens bij
    [InlineData(RuleKind.NameIs, "notities.txt", "Notities.txt", false, true)]
    [InlineData(RuleKind.NameIs, "notities", "Notities.txt", false, false)]
    [InlineData(RuleKind.Folder, "", "Projecten", true, true)]
    [InlineData(RuleKind.Folder, "", "a.txt", false, false)]
    public void A_rule_matches_by_extension_name_or_folder(RuleKind kind, string value, string name, bool folder, bool expected) =>
        Assert.Equal(expected, new FileRule { Kind = kind, Value = value }.Matches(name, folder));

    [Fact]
    public void New_items_go_to_the_pane_of_the_first_matching_rule()
    {
        var docs = new Pane { Title = "Documenten" };
        var folders = new Pane { Title = "Mappen" };
        var inbox = new Pane { IsInbox = true };
        var layout = new Layout { Panes = [docs, folders, inbox] };
        layout.Rules.Add(new FileRule { Kind = RuleKind.Extension, Value = "pdf", TargetPane = docs.Id });
        layout.Rules.Add(new FileRule { Kind = RuleKind.Folder, TargetPane = folders.Id });

        LayoutEditor.Sync(layout, ["user/a.pdf", "user/Werk", "user/b.txt", "shell/{645FF040-5081-101B-9F08-00AA002F954E}"],
            isFolder: id => id == "user/Werk");

        Assert.Equal(["user/a.pdf"], docs.Items);
        Assert.Equal(["user/Werk"], folders.Items);
        Assert.Equal(["shell/{645FF040-5081-101B-9F08-00AA002F954E}", "user/b.txt"], inbox.Items);
    }

    [Fact]
    public void A_pane_you_pasted_into_wins_over_the_rules()
    {
        var docs = new Pane();
        var chosen = new Pane();
        var layout = new Layout { Panes = [docs, chosen, new Pane { IsInbox = true }] };
        layout.Rules.Add(new FileRule { Kind = RuleKind.Extension, Value = "pdf", TargetPane = docs.Id });

        LayoutEditor.Sync(layout, ["user/a.pdf"], newItemsPane: chosen.Id);

        Assert.Empty(docs.Items);
        Assert.Equal(["user/a.pdf"], chosen.Items);
    }

    [Fact]
    public void Rules_to_a_task_pane_or_a_missing_pane_are_skipped()
    {
        var tasks = new Pane { Kind = PaneKind.Tasks };
        var docs = new Pane();
        var inbox = new Pane { IsInbox = true };
        var layout = new Layout { Panes = [tasks, docs, inbox] };
        layout.Rules.Add(new FileRule { Kind = RuleKind.Extension, Value = "pdf", TargetPane = Guid.NewGuid() });
        layout.Rules.Add(new FileRule { Kind = RuleKind.Extension, Value = "pdf", TargetPane = tasks.Id });
        layout.Rules.Add(new FileRule { Kind = RuleKind.Extension, Value = "pdf", TargetPane = docs.Id });

        LayoutEditor.Sync(layout, ["user/a.pdf"]);

        Assert.Equal(["user/a.pdf"], docs.Items);
    }

    [Fact]
    public void Applying_rules_sorts_what_is_already_in_the_inbox()
    {
        var docs = new Pane();
        var inbox = new Pane { IsInbox = true, Items = ["user/a.pdf", "user/b.txt", "user/c.PDF"] };
        var layout = new Layout { Panes = [docs, inbox] };
        layout.Rules.Add(new FileRule { Kind = RuleKind.Extension, Value = "pdf", TargetPane = docs.Id });

        Assert.Equal(2, LayoutEditor.ApplyRulesToInbox(layout));
        Assert.Equal(["user/a.pdf", "user/c.PDF"], docs.Items);
        Assert.Equal(["user/b.txt"], inbox.Items);
    }

    [Fact]
    public void Deleting_a_pane_removes_its_rules()
    {
        var docs = new Pane();
        var layout = new Layout { Panes = [docs, new Pane { IsInbox = true }] };
        layout.Rules.Add(new FileRule { TargetPane = docs.Id, Value = "pdf" });

        LayoutEditor.DeletePane(layout, docs.Id);

        Assert.Empty(layout.Rules);
    }
}

public sealed class TabTests
{
    [Fact]
    public void Adding_a_tab_puts_the_pane_and_its_own_tabs_in_the_target_window()
    {
        var a = new Pane { Title = "A" };
        var b = new Pane { Title = "B" };
        var c = new Pane { Title = "C", TabHost = null };
        var layout = new Layout { Panes = [a, b, c] };
        LayoutEditor.AddTab(layout, b.Id, c.Id); // B heeft nu C als tabblad

        var host = LayoutEditor.AddTab(layout, a.Id, b.Id);

        Assert.Same(a, host);
        Assert.Equal([a, b, c], LayoutEditor.TabsOf(layout, a));
        Assert.Same(b, LayoutEditor.ActiveTabOf(layout, a));
        Assert.Null(LayoutEditor.AddTab(layout, c.Id, a.Id)); // al samen
    }

    [Fact]
    public void Detaching_a_tab_gives_it_its_own_window_next_to_the_old_one()
    {
        var a = new Pane { Left = 100, Top = 200, Width = 300, Height = 250 };
        var b = new Pane();
        var layout = new Layout { Panes = [a, b] };
        LayoutEditor.AddTab(layout, a.Id, b.Id);

        var remaining = LayoutEditor.DetachTab(layout, b.Id);

        Assert.Same(a, remaining);
        Assert.Null(b.TabHost);
        Assert.Null(a.ActiveTab);
        Assert.Equal((130.0, 230.0, 300.0, 250.0), (b.Left, b.Top, b.Width, b.Height));
    }

    [Fact]
    public void Detaching_the_first_tab_hands_the_window_to_the_next_one()
    {
        var a = new Pane { Left = 100, Top = 200, Width = 300, Height = 250, IsRolledUp = true };
        var b = new Pane();
        var c = new Pane();
        var layout = new Layout { Panes = [a, b, c] };
        LayoutEditor.AddTab(layout, a.Id, b.Id);
        LayoutEditor.AddTab(layout, a.Id, c.Id); // C staat open

        var remaining = LayoutEditor.DetachTab(layout, a.Id);

        Assert.Same(b, remaining);
        Assert.Null(b.TabHost);
        Assert.Equal(b.Id, c.TabHost);
        Assert.Same(c, LayoutEditor.ActiveTabOf(layout, b));
        Assert.Equal((100.0, 200.0, true), (b.Left, b.Top, b.IsRolledUp));
        Assert.False(a.IsRolledUp);
        Assert.Null(a.TabHost);
    }

    [Fact]
    public void Deleting_the_window_pane_keeps_the_other_tabs_together_in_place()
    {
        var a = new Pane { Left = 100, Top = 200 };
        var b = new Pane();
        var inbox = new Pane { IsInbox = true };
        var layout = new Layout { Panes = [a, b, inbox] };
        LayoutEditor.AddTab(layout, a.Id, b.Id);

        Assert.True(LayoutEditor.DeletePane(layout, a.Id));

        Assert.Null(b.TabHost);
        Assert.Equal((100.0, 200.0), (b.Left, b.Top));
    }

    [Fact]
    public void Repair_frees_tabs_whose_window_is_gone_or_is_itself_a_tab()
    {
        var a = new Pane();
        var b = new Pane { TabHost = a.Id };
        var c = new Pane { TabHost = b.Id };           // in een tabblad: kan niet
        var d = new Pane { TabHost = Guid.NewGuid() }; // venster bestaat niet
        var e = new Pane();
        e.TabHost = e.Id;                              // in zichzelf
        var layout = new Layout { Panes = [a, b, c, d, e] };

        Assert.True(LayoutEditor.Repair(layout));

        Assert.Equal(a.Id, b.TabHost);
        Assert.Null(c.TabHost);
        Assert.Null(d.TabHost);
        Assert.Null(e.TabHost);
    }
}

public sealed class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Space", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Space")]
    [InlineData("win + shift + d", HotkeyModifiers.Win | HotkeyModifiers.Shift, "d")]
    public void A_hotkey_is_read_from_text(string text, HotkeyModifiers modifiers, string key)
    {
        var hotkey = Hotkey.Parse(text);
        Assert.Equal(new Hotkey(modifiers, key), hotkey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Space")]        // geen Ctrl, Alt of Win
    [InlineData("Shift+A")]
    [InlineData("Ctrl+Alt")]     // geen toets
    [InlineData("Ctrl+A+B")]     // twee toetsen
    public void Invalid_hotkeys_are_none(string? text) => Assert.Null(Hotkey.Parse(text));

    [Fact]
    public void A_hotkey_is_written_in_a_fixed_order() =>
        Assert.Equal("Ctrl+Alt+Shift+Win+F12", new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Ctrl, "F12").ToString());
}

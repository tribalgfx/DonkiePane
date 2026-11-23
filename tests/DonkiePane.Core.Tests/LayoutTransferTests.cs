using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class LayoutTransferTests
{
    private static Layout Sample() => new()
    {
        Panes =
        [
            new Pane { Title = "Programma's", Left = 40, Top = 60, Width = 352, Height = 288, Theme = "groen", IconSize = IconSize.Small, Items = ["d/kladblok.lnk"] },
            new Pane { Title = "Inbox", IsInbox = true },
            Pane.NewTaskPane(),
        ],
        SnapToGrid = false,
        GridSize = 48,
        HideOnDesktopDoubleClick = false,
        SyncSeconds = 120,
        Language = "en",
        NotifiedUpdateVersion = "0.3.3",
        WelcomeShown = true,
        DueRemindersDate = "2026-11-23",
        DueRemindedTaskIds = ["local/l/t"],
    };

    [Fact]
    public void An_exported_layout_comes_back_with_its_panes_and_settings()
    {
        var original = Sample();
        var imported = LayoutTransfer.Parse(LayoutTransfer.Serialize(original));

        Assert.Equal(original.Panes.Select(p => (p.Id, p.Title, p.Left, p.Top, p.Width, p.Height, p.Theme, p.IconSize, p.Kind)),
            imported.Panes.Select(p => (p.Id, p.Title, p.Left, p.Top, p.Width, p.Height, p.Theme, p.IconSize, p.Kind)));
        Assert.Equal(["d/kladblok.lnk"], imported.Panes[0].Items);
        Assert.Equal((false, 48d, false, 120), (imported.SnapToGrid, imported.GridSize, imported.HideOnDesktopDoubleClick, imported.SyncSeconds));
    }

    [Fact]
    public void What_only_matters_on_this_pc_is_not_exported()
    {
        var json = LayoutTransfer.Serialize(Sample());

        Assert.DoesNotContain("0.3.3", json);
        Assert.DoesNotContain("2026-11-23", json);
        Assert.DoesNotContain("local/l/t", json);
        Assert.Null(LayoutTransfer.Parse(json).Language);
    }

    [Fact]
    public void Exporting_does_not_change_the_layout_itself()
    {
        var layout = Sample();
        LayoutTransfer.Serialize(layout);

        Assert.Equal(("en", "0.3.3", 1), (layout.Language, layout.NotifiedUpdateVersion, layout.DueRemindedTaskIds.Count));
    }

    [Theory]
    [InlineData("dit is geen json")]
    [InlineData("""{"Panes":[]}""")]
    [InlineData("""{"Panes":null}""")]
    [InlineData("""{"Sources":[{"Id":"x"}]}""")] // een ander bestand van DonkiePane
    [InlineData("""[1,2,3]""")]
    public void Something_that_is_not_a_layout_is_refused(string json) =>
        Assert.Throws<InvalidDataException>(() => LayoutTransfer.Parse(json));

    [Fact]
    public void Importing_replaces_panes_and_settings_but_keeps_what_belongs_to_this_pc()
    {
        var current = new Layout
        {
            Panes = [new Pane { Title = "Oud" }],
            Language = "nl",
            CheckForUpdates = false,
            NotifiedUpdateVersion = "0.3.2",
            DueRemindersDate = "2026-11-22",
        };
        var imported = Sample();

        LayoutTransfer.ApplyTo(current, imported);

        Assert.Equal(["Programma's", "Inbox", Loc.T("Taken")], current.Panes.Select(p => p.Title));
        Assert.Equal((false, 48d, 120), (current.SnapToGrid, current.GridSize, current.SyncSeconds));
        Assert.Equal(("nl", false, "0.3.2", "2026-11-22"), (current.Language, current.CheckForUpdates, current.NotifiedUpdateVersion, current.DueRemindersDate));
    }
}

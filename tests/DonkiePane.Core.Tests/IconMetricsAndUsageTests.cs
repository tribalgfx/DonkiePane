using System.Text.Json;
using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class IconMetricsAndUsageTests
{
    [Theory]
    [InlineData(IconSize.Large, null, 48)]
    [InlineData(IconSize.Small, null, 32)]
    [InlineData(IconSize.List, null, 20)]
    [InlineData(IconSize.Large, 64, 64)]
    [InlineData(IconSize.Large, 500, IconMetrics.Max)] // met de hand aangepast bestand: binnen de grenzen
    [InlineData(IconSize.Small, 2, IconMetrics.Min)]
    public void The_icon_size_is_the_pane_choice_or_the_view_default(IconSize mode, int? custom, int expected) =>
        Assert.Equal(expected, IconMetrics.Pixels(mode, custom));

    [Theory]
    [InlineData(48, 1, 64)]
    [InlineData(48, -1, 40)]
    [InlineData(96, 1, 96)]  // de rand: blijft staan
    [InlineData(16, -1, 16)]
    [InlineData(50, 1, 64)]  // tussen twee stappen: naar de eerstvolgende
    [InlineData(50, -1, 48)]
    [InlineData(46, 1, 48)]
    [InlineData(46, -1, 40)]
    [InlineData(48, 2, 80)]
    public void Larger_and_smaller_go_by_steps(int current, int steps, int expected) =>
        Assert.Equal(expected, IconMetrics.Step(current, steps));

    [Fact]
    public void Tiles_fit_the_icon_and_the_names()
    {
        Assert.Equal(100, IconMetrics.TileWidth(IconSize.Large, 48, showNames: true));   // zoals altijd
        Assert.Equal(88, IconMetrics.TileWidth(IconSize.Small, 32, showNames: true));
        Assert.Equal(148, IconMetrics.TileWidth(IconSize.Large, 96, showNames: true));   // groot icoon: bredere tegel
        Assert.Equal(66, IconMetrics.TileWidth(IconSize.Large, 48, showNames: false));   // zonder namen: om het icoon
        Assert.True(double.IsNaN(IconMetrics.TileWidth(IconSize.List, 20, showNames: false)));
    }

    [Fact]
    public void An_old_layout_shows_names_at_the_view_size()
    {
        var pane = JsonSerializer.Deserialize<Pane>("""{ "Title": "Oud" }""")!;
        Assert.True(pane.ShowNames);
        Assert.Null(pane.IconPixels);
    }

    private static readonly DateTime Start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_new_item_counts_from_when_it_is_first_seen()
    {
        var usage = new ItemUsage();
        Assert.True(usage.Track(["desktop/a.txt"], Start));
        Assert.False(usage.Track(["desktop/a.txt"], Start.AddDays(5))); // al bekend: niets veranderd

        Assert.False(usage.IsUnused("desktop/a.txt", null, Start.AddDays(29), 30));
        Assert.True(usage.IsUnused("desktop/a.txt", null, Start.AddDays(30), 30));
        Assert.False(usage.IsUnused("desktop/a.txt", null, Start.AddDays(300), 0)); // uit
        Assert.False(usage.IsUnused("desktop/onbekend.txt", null, Start.AddDays(300), 30));
    }

    [Fact]
    public void Opening_or_changing_an_item_makes_it_used_again()
    {
        var usage = new ItemUsage();
        usage.Track(["desktop/a.txt", "desktop/b.txt"], Start);
        usage.Opened("DESKTOP/A.TXT", Start.AddDays(20)); // hoofdletters maken niet uit

        Assert.False(usage.IsUnused("desktop/a.txt", null, Start.AddDays(40), 30));
        Assert.True(usage.IsUnused("desktop/b.txt", null, Start.AddDays(40), 30));
        Assert.False(usage.IsUnused("desktop/b.txt", Start.AddDays(15), Start.AddDays(40), 30)); // gewijzigd
    }

    [Fact]
    public void Gone_items_are_forgotten_and_renamed_items_keep_their_history()
    {
        var usage = new ItemUsage();
        usage.Track(["desktop/a.txt", "desktop/b.txt"], Start);
        usage.Opened("desktop/a.txt", Start.AddDays(10));
        usage.Rename("desktop/a.txt", "desktop/c.txt");

        Assert.True(usage.Track(["desktop/c.txt"], Start.AddDays(11)));
        Assert.Equal(["desktop/c.txt"], usage.LastUsed.Keys);
        Assert.Equal(Start.AddDays(10), usage.LastUsed["desktop/c.txt"]);
    }

    [Fact]
    public void Usage_survives_saving_and_loading()
    {
        var path = Path.Combine(Path.GetTempPath(), "donkiepane-usage-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var usage = new ItemUsage();
            usage.Track(["desktop/a.txt"], Start);
            new ItemUsageStore(path).Save(usage);

            var loaded = new ItemUsageStore(path).Load();
            Assert.Equal(Start, loaded.LastUsed["DESKTOP/a.txt"]);
        }
        finally
        {
            foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(file);
        }
    }
}

public sealed class ReviewFixesTests
{
    [Theory]
    [InlineData("open", true)]
    [InlineData("OpenAs", true)]
    [InlineData("runas", true)]
    [InlineData("edit", true)]
    [InlineData(null, true)]        // bijv. een programma onder "Openen met"
    [InlineData("delete", false)]
    [InlineData("properties", false)]
    [InlineData("cut", false)]
    [InlineData("copy", false)]
    [InlineData("opencontaining", false)] // bestandslocatie openen gebruikt het item zelf niet
    public void Only_menu_options_that_use_the_item_count(string? verb, bool counts) =>
        Assert.Equal(counts, ItemUsage.CountsAsUse(verb));

    [Fact]
    public void A_drop_position_among_visible_icons_skips_items_that_are_not_shown()
    {
        // Twee items van een map die even weg is staan vooraan in de indeling, maar zijn niet te zien.
        string[] items = ["public/a", "public/b", "user/c", "user/d", "user/e"];
        string[] shown = ["user/c", "user/d", "user/e"];

        Assert.Equal(3, LayoutEditor.ToItemsIndex(items, shown, 1)); // vóór user/d
        Assert.Equal(2, LayoutEditor.ToItemsIndex(items, shown, 0));
        Assert.Equal(5, LayoutEditor.ToItemsIndex(items, shown, 3)); // achter het laatste
        Assert.Equal(-1, LayoutEditor.ToItemsIndex(items, shown, -1));
        Assert.Equal(-1, LayoutEditor.ToItemsIndex(items, [], 0));
    }

    [Theory]
    [InlineData(2, Layout.MinGridSize)]
    [InlineData(50_000, Layout.MaxGridSize)]
    [InlineData(40, 40)]
    public void A_grid_size_outside_the_settings_range_is_repaired(double size, double expected)
    {
        var layout = new Layout { GridSize = size };
        LayoutEditor.Repair(layout);
        Assert.Equal(expected, layout.GridSize);
    }

    [Fact]
    public void Importing_brings_the_gray_icons_setting_along()
    {
        var target = new Layout();
        LayoutTransfer.ApplyTo(target, new Layout { Panes = [new Pane()], DimUnusedDays = 90 });
        Assert.Equal(90, target.DimUnusedDays);

        LayoutTransfer.ApplyTo(target, new Layout { Panes = [new Pane()], DimUnusedDays = -5 });
        Assert.Equal(0, target.DimUnusedDays);
    }
}

public sealed class FrostedGlassNameTests
{
    [Fact]
    public void A_layout_that_says_Mica_still_loads_as_frosted_glass_and_is_saved_the_same_way()
    {
        var path = Path.Combine(Path.GetTempPath(), "donkiepane-frosted-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """{ "Panes": [ { "Title": "Oud", "Material": "Mica" } ] }""");
            var store = new LayoutStore(path);
            var layout = store.Load();
            Assert.Equal(PaneMaterial.Frosted, layout.Panes[0].Material);

            store.Save(layout);
            Assert.Contains("\"Material\": \"Mica\"", File.ReadAllText(path)); // een oudere DonkiePane kan het nog lezen
            Assert.Contains("\"Mica\"", LayoutTransfer.Serialize(layout));
        }
        finally
        {
            foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(file);
        }
    }
}

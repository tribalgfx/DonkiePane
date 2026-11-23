using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class ItemSortTests
{
    private static readonly DateTime Day = new(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SortableItem[] Items =
    [
        new("a", "Foto 10.jpg", false, "jpg", Day.AddDays(-3)),
        new("b", "Projecten", true, "", Day.AddDays(-10)),
        new("c", "foto 2.jpg", false, "jpg", Day),
        new("d", "Kladblok", false, "lnk", Day.AddDays(-1)),
        new("e", "Archief", true, "", Day.AddDays(-2)),
        new("f", "Begroting.xlsx", false, "xlsx", Day.AddDays(-5)),
    ];

    [Fact]
    public void Own_order_stays_as_it_is() =>
        Assert.Equal(["a", "b", "c", "d", "e", "f"], ItemSort.Order(Items, ItemSortOrder.Manual));

    [Fact]
    public void By_name_puts_folders_first_and_counts_numbers_as_numbers() =>
        // Mappen eerst; "foto 2" vóór "Foto 10", hoofdletters maken niet uit.
        Assert.Equal(["e", "b", "f", "c", "a", "d"], ItemSort.Order(Items, ItemSortOrder.Name));

    [Fact]
    public void By_type_groups_extensions_after_the_folders() =>
        Assert.Equal(["e", "b", "c", "a", "d", "f"], ItemSort.Order(Items, ItemSortOrder.Type));

    [Fact]
    public void By_date_shows_the_newest_first() =>
        Assert.Equal(["c", "d", "e", "a", "f", "b"], ItemSort.Order(Items, ItemSortOrder.Modified));

    [Theory]
    [InlineData("Foto 2", "Foto 10", -1)]
    [InlineData("foto 010", "Foto 10", 0)]
    [InlineData("abc", "ABD", -1)]
    [InlineData("Map", "Map 1", -1)]
    [InlineData("2026 begroting", "begroting", -1)]
    public void Natural_comparison(string x, string y, int expected) =>
        Assert.Equal(expected, Math.Sign(ItemSort.NaturalComparer.Instance.Compare(x, y)));

    [Fact]
    public void View_and_sorting_are_saved_with_the_pane()
    {
        var path = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"), "layout.json");
        try
        {
            var store = new LayoutStore(path);
            store.Save(new Layout { Panes = [new Pane { IconSize = IconSize.List, SortBy = ItemSortOrder.Modified }] });

            var loaded = Assert.Single(store.Load().Panes);
            Assert.Equal((IconSize.List, ItemSortOrder.Modified), (loaded.IconSize, loaded.SortBy));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void An_older_layout_without_view_settings_gets_large_icons_in_own_order()
    {
        var pane = System.Text.Json.JsonSerializer.Deserialize<Pane>("""{"Title":"Oud","Items":["d/a"]}""")!;
        Assert.Equal((IconSize.Large, ItemSortOrder.Manual), (pane.IconSize, pane.SortBy));
    }
}

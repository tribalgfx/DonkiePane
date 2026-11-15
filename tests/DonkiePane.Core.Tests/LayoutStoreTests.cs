using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class LayoutStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "layout.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Load_returns_empty_layout_when_no_file_exists()
    {
        var layout = new LayoutStore(FilePath).Load();

        Assert.Empty(layout.Panes);
    }

    [Fact]
    public void Saved_panes_are_loaded_back()
    {
        var store = new LayoutStore(FilePath);
        var pane = new Pane { Title = "Werk", Left = 10, Top = 20, Width = 300, Height = 200, IsRolledUp = true };
        store.Save(new Layout { Panes = [pane] });

        var loaded = Assert.Single(store.Load().Panes);

        Assert.Equal(pane.Id, loaded.Id);
        Assert.Equal("Werk", loaded.Title);
        Assert.Equal((10d, 20d, 300d, 200d), (loaded.Left, loaded.Top, loaded.Width, loaded.Height));
        Assert.True(loaded.IsRolledUp);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_previous_version()
    {
        var store = new LayoutStore(FilePath);
        store.Save(new Layout { Panes = [new Pane { Title = "Eerste" }] });
        store.Save(new Layout { Panes = [new Pane { Title = "Tweede" }] });

        File.WriteAllText(FilePath, "{ kapot");

        Assert.Equal("Eerste", Assert.Single(store.Load().Panes).Title);
    }
}

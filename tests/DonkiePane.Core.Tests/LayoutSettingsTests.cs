using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class LayoutSettingsTests
{
    [Fact]
    public void The_default_check_interval_is_a_minute()
    {
        Assert.Equal(60, new Layout().SyncSeconds);
        Assert.Equal(TimeSpan.FromSeconds(60), new Layout().SyncInterval);
    }

    [Theory]
    [InlineData(5, 15)]      // te snel: niet harder dan de ondergrens
    [InlineData(0, 15)]
    [InlineData(-30, 15)]
    [InlineData(15, 15)]
    [InlineData(90, 90)]
    [InlineData(300, 300)]
    [InlineData(100_000, 300)] // een handmatig aangepast bestand mag niets slopen
    public void The_check_interval_stays_within_sensible_bounds(int seconds, int expected) =>
        Assert.Equal(TimeSpan.FromSeconds(expected), new Layout { SyncSeconds = seconds }.SyncInterval);

    [Fact]
    public void An_old_layout_without_the_setting_uses_the_default()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "layout.json");
            File.WriteAllText(file, """{ "Version": 1, "Panes": [] }""");

            Assert.Equal(60, new LayoutStore(file).Load().SyncSeconds);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

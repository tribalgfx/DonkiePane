using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class GridSnapTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 32)]   // precies halverwege: naar boven
    [InlineData(100, 96)]
    [InlineData(-10, 0)]
    [InlineData(-20, -32)] // monitors links van de hoofdmonitor hebben negatieve posities
    public void Position_snaps_to_nearest_line(double value, double expected) =>
        Assert.Equal(expected, GridSnap.Position(value, 32));

    [Theory]
    [InlineData(320, 160, 320)]
    [InlineData(330, 160, 320)]
    [InlineData(350, 160, 352)]
    [InlineData(100, 160, 160)] // onder het minimum: omhoog naar een hele cel
    [InlineData(150, 160, 160)]
    [InlineData(10, 170, 192)]  // minimum zelf is geen veelvoud: naar boven afronden
    public void Size_snaps_and_respects_minimum(double value, double min, double expected) =>
        Assert.Equal(expected, GridSnap.Size(value, 32, min));

    [Fact]
    public void Cell_of_zero_or_less_disables_snapping()
    {
        Assert.Equal(17.3, GridSnap.Position(17.3, 0));
        Assert.Equal(17.3, GridSnap.Size(17.3, 0, 10));
        Assert.Equal(10, GridSnap.Size(5, -1, 10));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(32, 0)]
    [InlineData(10, 22)]   // venster op x=10: eerste lijn op x=32, dus 22 verderop
    [InlineData(-10, 10)]  // venster op x=-10: eerste lijn op x=0, dus 10 verderop
    [InlineData(1920, 0)]
    public void Offset_gives_distance_to_first_line(double origin, double expected) =>
        Assert.Equal(expected, GridSnap.Offset(origin, 32));

    [Fact]
    public void Snapped_values_are_stable()
    {
        var once = GridSnap.Position(137.4, 32);
        Assert.Equal(once, GridSnap.Position(once, 32));
    }

    [Fact]
    public void Resize_moves_only_the_dragged_edges()
    {
        // Rechterrand slepen: links blijft staan, breedte wordt een hele cel.
        Assert.Equal((64.0, 32.0, 64.0 + 352, 32.0 + 200), GridSnap.Resize(64, 32, 64 + 350, 32 + 200,
            false, false, true, false, 32, 160, 100));

        // Linksboven slepen: rechts en onder blijven staan.
        Assert.Equal((320.0 - 192, 320.0 - 128, 320.0, 320.0), GridSnap.Resize(130, 200, 320, 320,
            true, true, false, false, 32, 160, 100));
    }

    [Fact]
    public void Resize_respects_minimum_and_rolled_up_height()
    {
        // Te smal: naar de kleinste hele cel boven het minimum (192 bij minimum 170); hoogte blijft (opgerold).
        Assert.Equal((0.0, 0.0, 192.0, 37.0), GridSnap.Resize(0, 0, 50, 37,
            false, false, true, true, 32, 170, 100, snapHeight: false));
    }
}

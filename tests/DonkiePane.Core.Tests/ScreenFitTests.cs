using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class ScreenFitTests
{
    private static readonly PxRect Main = new(0, 0, 1920, 1080);
    private static readonly PxRect Right = new(1920, 0, 2560, 1440);
    private static readonly PxRect Left = new(-1920, 0, 1920, 1080);

    [Fact]
    public void Pane_fully_on_screen_stays()
    {
        Assert.Null(ScreenFit.Fit(new PxRect(100, 100, 400, 300), [Main]));
    }

    [Fact]
    public void Pane_partly_off_screen_but_reachable_stays()
    {
        // Rechts half buiten beeld: de linkerhelft is nog te pakken.
        Assert.Null(ScreenFit.Fit(new PxRect(1800, 100, 400, 300), [Main]));
    }

    [Fact]
    public void Pane_on_second_monitor_stays_while_it_exists()
    {
        Assert.Null(ScreenFit.Fit(new PxRect(2500, 200, 400, 300), [Main, Right]));
    }

    [Fact]
    public void Pane_on_removed_monitor_moves_to_the_remaining_one()
    {
        var moved = ScreenFit.Fit(new PxRect(2500, 200, 400, 300), [Main]);

        Assert.NotNull(moved);
        Assert.True(ScreenFit.IsReachable(moved.Value, [Main]));
        Assert.Equal((400, 300), (moved.Value.Width, moved.Value.Height)); // afmetingen blijven
        Assert.Equal(200, moved.Value.Y);                                  // hoogte blijft zo veel mogelijk
        Assert.Equal(1920 - 400, moved.Value.X);                           // tegen de rechterrand
    }

    [Fact]
    public void Pane_on_removed_left_monitor_lands_at_the_left_edge()
    {
        var moved = ScreenFit.Fit(new PxRect(-1500, 100, 400, 300), [Main]);

        Assert.Equal(new PxRect(0, 100, 400, 300), moved);
    }

    [Fact]
    public void Pane_moves_to_the_nearest_of_several_screens()
    {
        // Ver rechts van alles: het rechterscherm is het dichtstbij.
        var moved = ScreenFit.Fit(new PxRect(9000, 100, 400, 300), [Left, Main, Right]);

        Assert.NotNull(moved);
        Assert.True(moved.Value.X >= Right.X && moved.Value.Right <= Right.Right);
    }

    [Fact]
    public void Pane_below_the_screen_is_pulled_up()
    {
        var moved = ScreenFit.Fit(new PxRect(100, 5000, 400, 300), [Main]);

        Assert.Equal(new PxRect(100, 1080 - 300, 400, 300), moved);
    }

    [Fact]
    public void Pane_larger_than_the_screen_goes_to_the_top_left()
    {
        var moved = ScreenFit.Fit(new PxRect(5000, 5000, 3000, 2000), [Main]);

        Assert.Equal(new PxRect(0, 0, 3000, 2000), moved);
    }

    [Fact]
    public void Small_pane_needs_only_its_own_size_visible()
    {
        // Een opgerold pane (laag) mag niet "onbereikbaar" lijken omdat hij kleiner is dan de minimumzichtbaarheid.
        Assert.Null(ScreenFit.Fit(new PxRect(100, 100, 300, 34), [Main]));
    }

    [Fact]
    public void No_screens_means_no_change()
    {
        Assert.Null(ScreenFit.Fit(new PxRect(5000, 5000, 400, 300), []));
    }

    [Fact]
    public void Fitted_result_is_stable()
    {
        var once = ScreenFit.Fit(new PxRect(5000, 5000, 400, 300), [Main])!.Value;

        Assert.Null(ScreenFit.Fit(once, [Main]));
    }
}

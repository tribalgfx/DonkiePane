using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class DoubleClickDetectorTests
{
    private static DoubleClickDetector New() => new(maxIntervalMs: 500, maxDistanceX: 2, maxDistanceY: 2);

    [Fact]
    public void Two_quick_clicks_in_the_same_place_are_a_double_click()
    {
        var d = New();
        Assert.False(d.Register(1000, 300, 300));
        Assert.True(d.Register(1300, 301, 299));
    }

    [Fact]
    public void A_slow_second_click_is_not_a_double_click_but_starts_a_new_pair()
    {
        var d = New();
        Assert.False(d.Register(1000, 300, 300));
        Assert.False(d.Register(1600, 300, 300));
        Assert.True(d.Register(1900, 300, 300));
    }

    [Fact]
    public void A_second_click_too_far_away_is_not_a_double_click()
    {
        var d = New();
        Assert.False(d.Register(1000, 300, 300));
        Assert.False(d.Register(1100, 320, 300));
    }

    [Fact]
    public void A_third_quick_click_does_not_count_as_another_double_click()
    {
        var d = New();
        d.Register(1000, 10, 10);
        Assert.True(d.Register(1100, 10, 10));
        Assert.False(d.Register(1200, 10, 10)); // begint opnieuw
        Assert.True(d.Register(1300, 10, 10));
    }

    [Fact]
    public void Reset_forgets_the_previous_click()
    {
        var d = New();
        d.Register(1000, 10, 10);
        d.Reset(); // bijvoorbeeld: de eerste klik viel op een icoon of een venster
        Assert.False(d.Register(1100, 10, 10));
    }

    [Fact]
    public void A_clock_that_goes_backwards_is_never_a_double_click()
    {
        var d = New();
        d.Register(5000, 10, 10);
        Assert.False(d.Register(100, 10, 10));
    }

    [Fact]
    public void Hiding_panes_on_double_click_is_on_by_default_and_survives_an_old_file_without_the_setting()
    {
        Assert.True(new Layout().HideOnDesktopDoubleClick);
        var old = System.Text.Json.JsonSerializer.Deserialize<Layout>("""{"Version":1,"Panes":[]}""")!;
        Assert.True(old.HideOnDesktopDoubleClick);
    }
}

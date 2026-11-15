using System.Text.Json;
using DonkiePane.Core;

namespace DonkiePane.Core.Tests;

public sealed class PaneLookSettingsTests
{
    [Fact]
    public void Defaults_are_calm_with_some_transparency_and_no_pattern()
    {
        var pane = new Pane();

        Assert.Null(pane.CustomColor);
        Assert.False(pane.ShowPattern);
        Assert.Equal(Pane.DefaultOpacity, pane.EffectiveOpacity);
    }

    [Theory]
    [InlineData(5.0, 1.0)]
    [InlineData(-3.0, Pane.MinOpacity)]
    [InlineData(0.0, Pane.MinOpacity)]
    [InlineData(0.5, 0.5)]
    [InlineData(double.NaN, Pane.DefaultOpacity)]
    public void Opacity_is_kept_within_readable_limits(double stored, double expected)
    {
        Assert.Equal(expected, new Pane { Opacity = stored }.EffectiveOpacity);
    }

    [Fact]
    public void An_old_file_without_the_new_fields_still_loads_with_the_defaults()
    {
        var pane = JsonSerializer.Deserialize<Pane>("""{"Title":"Oud","Theme":"klassiek-rood"}""")!;

        Assert.Equal("klassiek-rood", pane.Theme);
        Assert.Equal(Pane.DefaultOpacity, pane.EffectiveOpacity);
        Assert.False(pane.ShowPattern);
    }

    [Fact]
    public void Colour_transparency_and_pattern_survive_saving_and_loading()
    {
        var pane = new Pane { CustomColor = "#2F9E6B", Opacity = 0.4, ShowPattern = true };

        var back = JsonSerializer.Deserialize<Pane>(JsonSerializer.Serialize(pane))!;

        Assert.Equal("#2F9E6B", back.CustomColor);
        Assert.Equal(0.4, back.Opacity);
        Assert.True(back.ShowPattern);
    }
}

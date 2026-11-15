using System.Windows;
using System.Windows.Media;
using DonkiePane.App;
using DonkiePane.Core;

namespace DonkiePane.App.Tests;

public sealed class PaneThemesTests
{
    private static ResourceDictionary Applied(Pane pane)
    {
        var resources = new ResourceDictionary();
        PaneThemes.Apply(resources, pane);
        return resources;
    }

    private static Color ColorOf(ResourceDictionary r, string key) => ((SolidColorBrush)r[key]).Color;

    [Fact]
    public void A_new_pane_is_calm_by_default_plain_anthracite_without_a_pattern()
    {
        var look = PaneThemes.Resolve(new Pane());

        Assert.Equal("antraciet", PaneThemes.DefaultId);
        Assert.Equal(PatternKind.None, look.Pattern);
        Assert.IsType<SolidColorBrush>(Applied(new Pane())["PaneBackground"]); // effen, geen ruitjes
    }

    [Fact]
    public void Every_theme_is_plain_unless_the_pattern_is_switched_on()
    {
        foreach (var theme in PaneThemes.All)
        {
            var plain = Applied(new Pane { Theme = theme.Id });
            var patterned = Applied(new Pane { Theme = theme.Id, ShowPattern = true });

            Assert.IsType<SolidColorBrush>(plain["PaneBackground"]);
            Assert.IsType<DrawingBrush>(patterned["PaneBackground"]);
        }
    }

    [Fact]
    public void Transparency_changes_the_background_but_never_the_title_text()
    {
        var solid = Applied(new Pane { Opacity = 1.0 });
        var glassy = Applied(new Pane { Opacity = 0.3 });

        Assert.Equal(255, ColorOf(solid, "PaneBackground").A);
        Assert.InRange(ColorOf(glassy, "PaneBackground").A, 76, 77); // 30% ondoorzichtig
        Assert.Equal(255, ColorOf(glassy, "PaneTitleText").A);
        Assert.True(ColorOf(glassy, "PaneTitleBackground").A > ColorOf(glassy, "PaneBackground").A); // titelbalk iets stevig
    }

    [Fact]
    public void A_custom_colour_replaces_the_theme_for_the_title_and_darkens_the_background()
    {
        var look = PaneThemes.Resolve(new Pane { Theme = "klassiek-rood", CustomColor = "#2F9E6B" });

        Assert.Equal("#2F9E6B", look.Title);
        Assert.NotEqual(PaneThemes.Get("klassiek-rood").Base, look.Base);
        Assert.True(PaneThemes.TryParseColor(look.Base, out var basis));
        Assert.True(basis.G < 0x40); // donkere variant, de tekst in het pane blijft leesbaar
    }

    [Fact]
    public void A_light_custom_colour_gets_dark_title_text_and_a_dark_one_gets_light_text()
    {
        Assert.Equal("#1B1C1E", PaneThemes.Resolve(new Pane { CustomColor = "#F2E6A0" }).TitleText);
        Assert.Equal("#F5F0E8", PaneThemes.Resolve(new Pane { CustomColor = "#204060" }).TitleText);
    }

    [Fact]
    public void A_nearly_black_custom_colour_still_gets_a_visible_checkbox()
    {
        var look = PaneThemes.Resolve(new Pane { CustomColor = "#050505" });

        Assert.True(PaneThemes.TryParseColor(look.Check, out var check));
        Assert.True(check.R > 0x40);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rood")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    public void A_broken_custom_colour_is_ignored_and_the_theme_is_used(string? broken)
    {
        var look = PaneThemes.Resolve(new Pane { Theme = "blauw", CustomColor = broken });

        Assert.Equal(PaneThemes.Get("blauw").Title, look.Title);
    }

    [Fact]
    public void An_unknown_theme_name_falls_back_to_the_default()
    {
        Assert.Equal(PaneThemes.DefaultId, PaneThemes.Get("bestaat-niet").Id);
        Assert.Equal("klassiek-rood", PaneThemes.Get("klassiek-rood").Id); // bestaande panes houden hun thema
    }
}

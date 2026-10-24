using Beetle.Core;
using Xunit;

namespace Beetle.Tests;

public class BoostTests
{
    [Fact]
    public void Zero_boost_leaves_the_colour_alone()
    {
        var c = new RgbColor(100, 20, 30);
        Assert.Equal(c, c.Boost(0));
    }

    [Fact]
    public void Dark_red_becomes_a_brighter_red()
    {
        var boosted = new RgbColor(100, 0, 0).Boost(1);
        Assert.True(boosted.R > 150, $"expected a bright red, got {boosted.ToHex()}");
        Assert.Equal(0, boosted.G);
        Assert.Equal(0, boosted.B);
    }

    [Fact]
    public void Dark_grey_stays_dark_instead_of_glowing()
    {
        var grey = new RgbColor(40, 40, 40);
        Assert.Equal(grey, grey.Boost(1));
    }

    [Fact]
    public void Black_stays_black()
    {
        Assert.Equal(RgbColor.Black, RgbColor.Black.Boost(1));
    }

    [Fact]
    public void More_boost_is_never_dimmer()
    {
        var c = new RgbColor(90, 40, 20);
        var weak = c.Boost(0.3).ToHsv();
        var strong = c.Boost(0.9).ToHsv();
        Assert.True(strong.Value >= weak.Value);
        Assert.True(strong.Saturation >= weak.Saturation);
    }

    [Fact]
    public void Hue_is_preserved()
    {
        var c = new RgbColor(30, 80, 120);
        Assert.InRange(Math.Abs(c.ToHsv().Hue - c.Boost(0.8).ToHsv().Hue), 0, 2);
    }

    [Fact]
    public void Out_of_range_amounts_are_clamped()
    {
        var c = new RgbColor(100, 0, 0);
        Assert.Equal(c.Boost(1), c.Boost(5));
        Assert.Equal(c, c.Boost(-1));
    }
}

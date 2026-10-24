using Beetle.Core;
using Xunit;

namespace Beetle.Tests;

public class RgbColorTests
{
    [Theory]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("00FF00", 0, 255, 0)]
    [InlineData("#1234ab", 0x12, 0x34, 0xAB)]
    public void TryParseHex_reads_six_digit_colors(string text, int r, int g, int b)
    {
        Assert.True(RgbColor.TryParseHex(text, out var color));
        Assert.Equal(new RgbColor((byte)r, (byte)g, (byte)b), color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#FFF")]
    [InlineData("nothex!")]
    [InlineData("#1234567")]
    public void TryParseHex_rejects_bad_input(string? text)
    {
        Assert.False(RgbColor.TryParseHex(text, out _));
    }

    [Fact]
    public void ToHex_roundtrips()
    {
        var color = new RgbColor(0xAB, 0xCD, 0xEF);
        Assert.True(RgbColor.TryParseHex(color.ToHex(), out var parsed));
        Assert.Equal(color, parsed);
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]     // red
    [InlineData(120, 0, 255, 0)]   // green
    [InlineData(240, 0, 0, 255)]   // blue
    public void FromHsv_produces_primary_colors(double hue, int r, int g, int b)
    {
        Assert.Equal(new RgbColor((byte)r, (byte)g, (byte)b), RgbColor.FromHsv(hue, 1, 1));
    }

    [Fact]
    public void Hsv_roundtrips_through_rgb()
    {
        foreach (var original in new[] { new RgbColor(200, 40, 90), new RgbColor(10, 180, 240), new RgbColor(128, 128, 128) })
        {
            var (h, s, v) = original.ToHsv();
            var back = RgbColor.FromHsv(h, s, v);
            Assert.True(original.MaxChannelDelta(back) <= 1, $"{original.ToHex()} -> {back.ToHex()}");
        }
    }

    [Fact]
    public void Saturate_keeps_grey_grey()
    {
        var grey = new RgbColor(100, 100, 100);
        Assert.Equal(grey, grey.Saturate(2.0));
    }

    [Fact]
    public void LerpTo_moves_halfway()
    {
        Assert.Equal(new RgbColor(50, 50, 50), RgbColor.Black.LerpTo(new RgbColor(100, 100, 100), 0.5));
    }

    [Fact]
    public void LerpTo_endpoints_are_exact()
    {
        var a = new RgbColor(10, 20, 30);
        var b = new RgbColor(200, 100, 60);
        Assert.Equal(a, a.LerpTo(b, 0));
        Assert.Equal(b, a.LerpTo(b, 1));
    }
}

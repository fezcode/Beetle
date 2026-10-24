using Beetle.Core;
using Xunit;

namespace Beetle.Tests;

public class ColorAnalysisTests
{
    // Builds a BGRA pixel buffer, the layout GDI returns, from RGB colours repeated as given.
    private static byte[] Pixels(params (RgbColor Color, int Count)[] runs)
    {
        var bytes = new List<byte>();
        foreach (var (color, count) in runs)
            for (var i = 0; i < count; i++) bytes.AddRange([color.B, color.G, color.R, 255]);
        return [.. bytes];
    }

    private static readonly RgbColor Red = new(220, 30, 40);
    private static readonly RgbColor White = new(255, 255, 255);
    private static readonly RgbColor Black = new(0, 0, 0);
    private static readonly RgbColor Grey = new(128, 128, 128);

    [Fact]
    public void Average_counts_every_pixel()
    {
        Assert.Equal(new RgbColor(127, 127, 127), ColorAnalysis.Average(Pixels((Black, 1), (White, 1))));
    }

    [Fact]
    public void Selective_ignores_black_white_and_grey_and_keeps_the_colour()
    {
        // A small red logo on a mostly white page with black text and grey chrome.
        var page = Pixels((White, 700), (Black, 100), (Grey, 150), (Red, 50));
        Assert.Equal(Red, ColorAnalysis.Selective(page));
        // The plain average of the same page is a washed-out pinkish grey, which is the problem.
        var average = ColorAnalysis.Average(page);
        Assert.True(average.ToHsv().Saturation < 0.15, $"average {average.ToHex()} should be nearly grey");
    }

    [Fact]
    public void Selective_averages_several_colours_that_remain()
    {
        var blue = new RgbColor(0, 0, 200);
        Assert.Equal(new RgbColor(110, 15, 120), ColorAnalysis.Selective(Pixels((White, 50), (Red, 1), (blue, 1))));
    }

    [Fact]
    public void Selective_reports_nothing_for_a_screen_without_colour()
    {
        Assert.Null(ColorAnalysis.Selective(Pixels((White, 10), (Black, 10), (Grey, 10), (new RgbColor(200, 205, 210), 10))));
        Assert.Null(ColorAnalysis.Selective([]));
    }

    [Theory]
    [InlineData(255, 255, 255, false)] // white
    [InlineData(0, 0, 0, false)]       // black
    [InlineData(90, 90, 90, false)]    // grey
    [InlineData(230, 225, 215, false)] // warm off-white: too little saturation
    [InlineData(30, 5, 5, false)]      // red so dark it reads as black
    [InlineData(120, 20, 20, true)]    // dark red that is still clearly red
    [InlineData(255, 140, 0, true)]    // orange
    [InlineData(60, 200, 255, true)]   // sky blue
    public void IsChromatic_separates_colour_from_tones(byte r, byte g, byte b, bool expected)
    {
        Assert.Equal(expected, ColorAnalysis.IsChromatic(r, g, b));
    }

    [Fact]
    public void Reduce_dispatches_by_mode()
    {
        var page = Pixels((White, 9), (Red, 1));
        Assert.Equal(ColorAnalysis.Average(page), ColorAnalysis.Reduce(page, AmbientColorMode.Average));
        Assert.Equal(Red, ColorAnalysis.Reduce(page, AmbientColorMode.Selective));
    }

    [Fact]
    public void Settings_round_trip_the_mode_and_default_to_average()
    {
        Assert.Equal(AmbientColorMode.Average, new AppSettings().ColorMode);
        Assert.Equal(AmbientColorMode.Selective, new AppSettings { ColorMode = AmbientColorMode.Selective }.ColorMode);
        Assert.Equal(AmbientColorMode.Average, new AppSettings { AmbientMode = "nonsense" }.ColorMode);
    }
}

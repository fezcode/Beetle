using Beetle.Core;
using Xunit;

namespace Beetle.Tests;

public class LedCommandsTests
{
    [Fact]
    public void PowerOn_matches_the_known_frame()
    {
        Assert.Equal(new byte[] { 0x7E, 0x00, 0x04, 0xF0, 0x00, 0x01, 0xFF, 0x00, 0xEF }, LedCommands.Power(true));
    }

    [Fact]
    public void PowerOff_matches_the_known_frame()
    {
        Assert.Equal(new byte[] { 0x7E, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0x00, 0xEF }, LedCommands.Power(false));
    }

    [Fact]
    public void Color_places_rgb_in_bytes_four_through_six()
    {
        Assert.Equal(new byte[] { 0x7E, 0x00, 0x05, 0x03, 0x12, 0x34, 0x56, 0x00, 0xEF }, LedCommands.Color(new RgbColor(0x12, 0x34, 0x56)));
    }

    [Fact]
    public void PureColors_land_in_the_right_channel()
    {
        Assert.Equal(0xFF, LedCommands.Color(new RgbColor(255, 0, 0))[4]);
        Assert.Equal(0xFF, LedCommands.Color(new RgbColor(0, 255, 0))[5]);
        Assert.Equal(0xFF, LedCommands.Color(new RgbColor(0, 0, 255))[6]);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(-10, 0)]
    [InlineData(250, 100)]
    public void Brightness_clamps_to_0_100(int input, byte expected)
    {
        Assert.Equal(expected, LedCommands.Brightness(input)[3]);
    }

    [Fact]
    public void Every_frame_is_nine_bytes_bracketed_by_7E_and_EF()
    {
        foreach (var frame in new[] { LedCommands.Power(true), LedCommands.Power(false), LedCommands.Color(RgbColor.White), LedCommands.Brightness(42) })
        {
            Assert.Equal(9, frame.Length);
            Assert.Equal(0x7E, frame[0]);
            Assert.Equal(0xEF, frame[^1]);
        }
    }
}

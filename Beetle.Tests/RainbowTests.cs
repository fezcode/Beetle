using Beetle.Core;
using Xunit;

namespace Beetle.Tests;

file sealed class RecordingTransport : ILedTransport
{
    public List<byte[]> Frames { get; } = [];
    public bool IsConnected => true;
    public Task WriteAsync(byte[] frame, CancellationToken cancellationToken = default)
    {
        lock (Frames) Frames.Add(frame);
        return Task.CompletedTask;
    }
}

public class RainbowTests
{
    [Theory]
    [InlineData(0, 1, 10, 36)]     // a tenth of a 10 s cycle is 36 degrees
    [InlineData(350, 1, 10, 26)]   // wraps past 360
    [InlineData(90, 10, 10, 90)]   // a whole cycle comes back to the start
    [InlineData(180, 0, 10, 180)]  // no time, no movement
    public void Advance_moves_the_hue_by_the_share_of_a_cycle(double hue, double seconds, double cycle, double expected)
    {
        Assert.Equal(expected, RainbowController.Advance(hue, seconds, cycle), 6);
    }

    [Fact]
    public void Cycle_length_is_clamped_to_the_supported_range()
    {
        var rainbow = new RainbowController(new StripController(new RecordingTransport()));
        rainbow.CycleSeconds = 0.5;
        Assert.Equal(RainbowController.MinCycleSeconds, rainbow.CycleSeconds);
        rainbow.CycleSeconds = 999;
        Assert.Equal(RainbowController.MaxCycleSeconds, rainbow.CycleSeconds);
    }

    [Fact]
    public async Task Running_sends_fully_saturated_colours_that_change_over_time()
    {
        var transport = new RecordingTransport();
        var rainbow = new RainbowController(new StripController(transport)) { IntervalMs = 10, CycleSeconds = 3 };
        var shown = new List<RgbColor>();
        rainbow.Sampled += c => { lock (shown) shown.Add(c); };

        rainbow.Start();
        await Task.Delay(300);
        rainbow.Stop();

        Assert.False(rainbow.IsRunning);
        List<RgbColor> colours;
        lock (shown) colours = [.. shown];
        Assert.True(colours.Count > 3, $"expected several steps, got {colours.Count}");
        Assert.All(colours, c => Assert.InRange(c.ToHsv().Saturation, 0.99, 1.0));
        Assert.True(colours.Distinct().Count() > 1, "the hue should move");
        lock (transport.Frames)
            Assert.Contains(transport.Frames, f => f[2] == 0x05);   // colour frames went to the strip
    }
}

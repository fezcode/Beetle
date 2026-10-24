using Beetle.Core;
using Xunit;

namespace Beetle.Tests;

/// <summary>Records every frame it is handed, so controller behaviour can be checked without a strip.</summary>
file sealed class FakeTransport : ILedTransport
{
    public List<byte[]> Frames { get; } = [];
    public bool IsConnected => true;
    public Task WriteAsync(byte[] frame, CancellationToken cancellationToken = default)
    {
        Frames.Add(frame);
        return Task.CompletedTask;
    }
}

file sealed class FixedSampler(RgbColor color) : IScreenSampler
{
    public RgbColor Sample() => color;
    public void Dispose() { }
}

public class StripControllerTests
{
    [Fact]
    public async Task SetColor_while_off_powers_on_first_then_sends_colour()
    {
        var transport = new FakeTransport();
        var strip = new StripController(transport);

        await strip.SetColorAsync(new RgbColor(10, 20, 30));

        Assert.Equal(2, transport.Frames.Count);
        Assert.Equal(LedCommands.Power(true), transport.Frames[0]);
        Assert.Equal(LedCommands.Color(new RgbColor(10, 20, 30)), transport.Frames[1]);
        Assert.True(strip.IsOn);
    }

    [Fact]
    public async Task SetColor_while_on_sends_only_the_colour()
    {
        var transport = new FakeTransport();
        var strip = new StripController(transport);
        await strip.PowerAsync(true);
        transport.Frames.Clear();

        await strip.SetColorAsync(new RgbColor(1, 2, 3));

        Assert.Single(transport.Frames);
        Assert.Equal(LedCommands.Color(new RgbColor(1, 2, 3)), transport.Frames[0]);
    }

    [Fact]
    public async Task Power_and_brightness_track_state()
    {
        var strip = new StripController(new FakeTransport());
        await strip.PowerAsync(true);
        await strip.SetBrightnessAsync(70);
        Assert.True(strip.IsOn);
        Assert.Equal(70, strip.Brightness);
    }
}

public class AmbientControllerTests
{
    [Fact]
    public async Task Running_the_loop_sends_colour_frames_and_reports_samples()
    {
        var transport = new FakeTransport();
        var strip = new StripController(transport);
        var ambient = new AmbientController(strip, new FixedSampler(new RgbColor(200, 40, 60)))
        {
            IntervalMs = 10,
            Smoothing = 1.0, // jump straight to the sample so one tick is enough
        };
        var sampled = 0;
        ambient.Sampled += _ => Interlocked.Increment(ref sampled);

        ambient.Start();
        await Task.Delay(120);
        ambient.Stop();

        Assert.False(ambient.IsRunning);
        Assert.True(sampled > 0, "expected at least one sample");
        Assert.Contains(transport.Frames, f => f.Length == 9 && f[2] == 0x05); // a colour frame went out
    }
}

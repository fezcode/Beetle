using Beetle.Core;
using Xunit;

namespace Beetle.Tests;

public class PixelResampleTests
{
    private static byte[] Solid(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; pixels[i + 3] = 255; }
        return pixels;
    }

    [Fact]
    public void AreaAverage_KeepsASolidColour()
    {
        var src = Solid(240, 135, 200, 100, 50);
        var dst = new byte[128 * 72 * 4];
        PixelResample.AreaAverage(src, 240, 135, 240 * 4, dst, 128, 72);
        Assert.Equal(new RgbColor(200, 100, 50), ColorAnalysis.Average(dst));
    }

    [Fact]
    public void AreaAverage_AveragesTheBlockBehindEachPixel()
    {
        // 2x1 -> 1x1: black and white average to mid grey.
        byte[] src = [0, 0, 0, 255, 254, 254, 254, 255];
        var dst = new byte[4];
        PixelResample.AreaAverage(src, 2, 1, 8, dst, 1, 1);
        Assert.Equal([127, 127, 127, 255], dst);
    }

    [Fact]
    public void AreaAverage_HonoursTheRowStride()
    {
        // 1x2 image whose rows are 8 bytes apart; the padding must be skipped.
        byte[] src = [10, 20, 30, 255, 99, 99, 99, 99, 30, 40, 50, 255];
        var dst = new byte[4];
        PixelResample.AreaAverage(src, 1, 2, 8, dst, 1, 1);
        Assert.Equal([20, 30, 40, 255], dst);
    }

    [Fact]
    public void AreaAverage_RepeatsPixelsWhenTheSourceIsSmaller()
    {
        var src = Solid(10, 5, 0, 255, 0);
        var dst = new byte[128 * 72 * 4];
        PixelResample.AreaAverage(src, 10, 5, 40, dst, 128, 72);
        Assert.Equal(new RgbColor(0, 255, 0), ColorAnalysis.Average(dst));
    }

    [Fact]
    public void AreaAverage_LetsASmallColouredSpotSurvive()
    {
        // A grey 512x288 image with an 8x8 red spot: after shrinking to 128x72, selective mode still sees red.
        var src = Solid(512, 288, 128, 128, 128);
        for (var y = 100; y < 108; y++)
            for (var x = 200; x < 208; x++) { var i = (y * 512 + x) * 4; src[i] = 0; src[i + 1] = 0; src[i + 2] = 255; }
        var dst = new byte[128 * 72 * 4];
        PixelResample.AreaAverage(src, 512, 288, 512 * 4, dst, 128, 72);
        var selective = ColorAnalysis.Selective(dst);
        Assert.NotNull(selective);
        Assert.True(selective.Value.R > 200 && selective.Value.G < 60 && selective.Value.B < 60);
    }

    [Theory]
    [InlineData(3840, 2160, 4)]   // 240x135
    [InlineData(1920, 1080, 3)]   // 240x135
    [InlineData(2560, 1440, 4)]   // 160x90
    [InlineData(1920, 540, 2)]    // 480x135: height limits it
    [InlineData(100, 50, 0)]      // already smaller than the sample
    [InlineData(128, 72, 0)]
    [InlineData(256, 144, 1)]
    public void MipLevelFor_PicksTheSmallestLevelStillCoveringTheSample(int width, int height, int expected)
    {
        Assert.Equal(expected, PixelResample.MipLevelFor(width, height, 128, 72));
    }

}

public class ScreenSamplerTests
{
    private sealed class FakeGrab(params GrabResult[] results) : IScreenGrab
    {
        private int _next;
        public byte Fill { get; set; } = 200;
        public int Calls { get; private set; }
        public bool Disposed { get; private set; }

        public GrabResult Grab(SampleRegion region, Span<byte> bgra)
        {
            Calls++;
            var result = results[Math.Min(_next++, results.Length - 1)];
            if (result == GrabResult.New)
                for (var i = 0; i < bgra.Length; i += 4) { bgra[i] = 0; bgra[i + 1] = 0; bgra[i + 2] = Fill; bgra[i + 3] = 255; }
            return result;
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void UnchangedReusesThePreviousImage()
    {
        var grab = new FakeGrab(GrabResult.New, GrabResult.Unchanged);
        using var sampler = new ScreenSampler(() => grab, () => throw new InvalidOperationException("no fallback expected"));
        Assert.Equal(new RgbColor(200, 0, 0), sampler.Sample());
        Assert.Equal(new RgbColor(200, 0, 0), sampler.Sample());
    }

    [Fact]
    public void UnchangedBeforeAnyImageReportsNothing()
    {
        using var sampler = new ScreenSampler(() => new FakeGrab(GrabResult.Unchanged), () => new FakeGrab(GrabResult.New));
        Assert.Null(sampler.Sample());
    }

    [Fact]
    public void FailedReportsNothing()
    {
        using var sampler = new ScreenSampler(() => new FakeGrab(GrabResult.New, GrabResult.Failed), () => new FakeGrab(GrabResult.New));
        Assert.NotNull(sampler.Sample());
        Assert.Null(sampler.Sample());
        Assert.False(sampler.UsingFallback);
    }

    [Fact]
    public void MissingPrimaryUsesTheFallback()
    {
        using var sampler = new ScreenSampler(() => null, () => new FakeGrab(GrabResult.New));
        Assert.True(sampler.UsingFallback);
        Assert.Equal(new RgbColor(200, 0, 0), sampler.Sample());
    }

    [Fact]
    public void UnsupportedSwitchesToTheFallbackAndGrabsAgainAtOnce()
    {
        var primary = new FakeGrab(GrabResult.Unsupported);
        var fallback = new FakeGrab(GrabResult.New) { Fill = 90 };
        using var sampler = new ScreenSampler(() => primary, () => fallback);
        Assert.Equal(new RgbColor(90, 0, 0), sampler.Sample());
        Assert.True(sampler.UsingFallback);
        Assert.True(primary.Disposed);
    }

    [Fact]
    public void DisposedSamplerReportsNothingAndReleasesTheGrab()
    {
        var grab = new FakeGrab(GrabResult.New);
        var sampler = new ScreenSampler(() => grab, () => new FakeGrab(GrabResult.New));
        sampler.Dispose();
        Assert.True(grab.Disposed);
        Assert.Null(sampler.Sample());
    }
}

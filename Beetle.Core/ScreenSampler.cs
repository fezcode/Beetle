namespace Beetle.Core;

/// <summary>One representative colour for whatever is on screen right now.</summary>
public interface IScreenSampler : IDisposable
{
    /// <summary>The colour, or null when there is nothing to report: the grab failed, or selective
    /// mode found only black, white and greys. Ambient mode holds its last colour on null.</summary>
    RgbColor? Sample();
}

/// <summary>What one attempt to grab the screen produced.</summary>
public enum GrabResult
{
    /// <summary>The pixel buffer now holds a fresh image of the region.</summary>
    New,
    /// <summary>Nothing on screen changed since the last grab; the previous image still stands.</summary>
    Unchanged,
    /// <summary>No image this time (a desktop switch, a display change); worth trying again later.</summary>
    Failed,
    /// <summary>This grab method cannot work here at all; use another one.</summary>
    Unsupported,
}

/// <summary>A way of shrinking a region of the primary monitor to <see cref="ScreenSampler.SampleWidth"/> x
/// <see cref="ScreenSampler.SampleHeight"/> BGRA pixels. Called from one thread at a time.</summary>
public interface IScreenGrab : IDisposable
{
    GrabResult Grab(SampleRegion region, Span<byte> bgra);
}

/// <summary>
/// Samples the chosen <see cref="Region"/> of the primary monitor and reduces it to one colour by
/// <see cref="Mode"/> (see <see cref="ColorAnalysis"/>).
///
/// The grab prefers <see cref="DxgiScreenGrab"/>: Desktop Duplication hands over the desktop as a GPU
/// texture and the GPU shrinks it, so the CPU reads a few kilobytes instead of a whole 4K frame, and a
/// screen that hasn't changed costs nothing at all. Where that is unavailable (no hardware D3D11, a
/// rotated display, a remote session that refuses duplication) it falls back to <see cref="GdiScreenGrab"/>.
///
/// The image is fine enough (128x72) that a small coloured element survives the shrink as coloured
/// pixels instead of being blended into the grey around it, which selective mode depends on.
/// </summary>
public sealed class ScreenSampler : IScreenSampler
{
    public const int SampleWidth = 128;
    public const int SampleHeight = 72;

    private readonly Lock _gate = new();
    private readonly byte[] _pixels = new byte[SampleWidth * SampleHeight * 4];
    private readonly Func<IScreenGrab> _fallback;
    private IScreenGrab? _grab;
    private bool _usingFallback;
    private bool _hasPixels;
    private SampleRegion _region = SampleRegion.Full;
    private AmbientColorMode _mode = AmbientColorMode.Average;
    private bool _disposed;

    /// <summary>The DXGI grab with a GDI fallback: what the app uses.</summary>
    public ScreenSampler() : this(DxgiScreenGrab.TryCreate, () => new GdiScreenGrab()) { }

    /// <summary>For tests: <paramref name="primary"/> may return null, meaning "unavailable, use the fallback".</summary>
    public ScreenSampler(Func<IScreenGrab?> primary, Func<IScreenGrab> fallback)
    {
        _fallback = fallback;
        _grab = primary();
        if (_grab is null) UseFallback();
    }

    /// <summary>The part of the primary monitor to sample. Safe to change while sampling runs.</summary>
    public SampleRegion Region
    {
        get { lock (_gate) return _region; }
        set { lock (_gate) _region = value.Normalize(); }
    }

    /// <summary>How the sampled pixels become one colour. Safe to change while sampling runs.</summary>
    public AmbientColorMode Mode
    {
        get { lock (_gate) return _mode; }
        set { lock (_gate) _mode = value; }
    }

    /// <summary>True once the sampler has given up on the GPU grab and is using GDI.</summary>
    public bool UsingFallback { get { lock (_gate) return _usingFallback; } }

    public RgbColor? Sample()
    {
        // One lock for the whole sample: an ambient loop that is stopping can still be mid-grab when
        // the next one starts, and neither grab backend may be used from two threads at once.
        lock (_gate)
        {
            if (_disposed || _grab is null) return null;
            var result = _grab.Grab(_region, _pixels);
            if (result == GrabResult.Unsupported && !_usingFallback)
            {
                UseFallback();
                result = _grab.Grab(_region, _pixels);
            }
            switch (result)
            {
                case GrabResult.New: _hasPixels = true; break;
                case GrabResult.Unchanged when _hasPixels: break;
                default: return null;
            }
            return ColorAnalysis.Reduce(_pixels, _mode); // both grabs hand back BGRA
        }
    }

    private void UseFallback()
    {
        _grab?.Dispose();
        _grab = _fallback();
        _usingFallback = true;
        _hasPixels = false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _grab?.Dispose();
            _grab = null;
        }
    }
}

/// <summary>Pure pixel helpers for the grabs, tested without a screen.</summary>
public static class PixelResample
{
    /// <summary>
    /// Area-averages a BGRA image (rows <paramref name="srcStride"/> bytes apart) into a tightly packed
    /// <paramref name="dstWidth"/> x <paramref name="dstHeight"/> BGRA image. Each destination pixel is the
    /// mean of the source block behind it; when the source is smaller, pixels repeat.
    /// </summary>
    public static void AreaAverage(ReadOnlySpan<byte> src, int srcWidth, int srcHeight, int srcStride,
        Span<byte> dst, int dstWidth, int dstHeight)
    {
        if (srcWidth <= 0 || srcHeight <= 0) { dst.Clear(); return; }
        for (var dy = 0; dy < dstHeight; dy++)
        {
            var y0 = dy * srcHeight / dstHeight;
            var y1 = Math.Max(y0 + 1, (dy + 1) * srcHeight / dstHeight);
            for (var dx = 0; dx < dstWidth; dx++)
            {
                var x0 = dx * srcWidth / dstWidth;
                var x1 = Math.Max(x0 + 1, (dx + 1) * srcWidth / dstWidth);
                int b = 0, g = 0, r = 0;
                for (var y = y0; y < y1; y++)
                {
                    var row = y * srcStride;
                    for (var x = x0; x < x1; x++)
                    {
                        var i = row + x * 4;
                        b += src[i]; g += src[i + 1]; r += src[i + 2];
                    }
                }
                var count = (y1 - y0) * (x1 - x0);
                var o = (dy * dstWidth + dx) * 4;
                dst[o] = (byte)(b / count);
                dst[o + 1] = (byte)(g / count);
                dst[o + 2] = (byte)(r / count);
                dst[o + 3] = 255;
            }
        }
    }

    /// <summary>The smallest mip level of a <paramref name="width"/> x <paramref name="height"/> texture that
    /// is still at least <paramref name="minWidth"/> x <paramref name="minHeight"/> (0 when even the full size
    /// is smaller). Mip level n is (width &gt;&gt; n) x (height &gt;&gt; n), each at least 1.</summary>
    public static int MipLevelFor(int width, int height, int minWidth, int minHeight)
    {
        var level = 0;
        while ((width >> (level + 1)) >= minWidth && (height >> (level + 1)) >= minHeight) level++;
        return level;
    }
}

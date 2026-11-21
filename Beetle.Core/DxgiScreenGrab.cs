using System.Diagnostics;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Beetle.Core;

/// <summary>
/// The fast grab, through DXGI Desktop Duplication. Windows hands over the primary monitor's desktop as
/// a GPU texture, and the shrink happens on the GPU too: the region is copied into a mipmapped texture,
/// the GPU builds its mips (each a 2x2 box average of the one above, so it averages like a halftone
/// stretch), and only the smallest mip that is still at least 128x72 is read back, a few kilobytes. The
/// CPU then area-averages that to exactly 128x72.
///
/// Duplication also says when nothing changed: no new frame means the previous image stands, so a still
/// screen costs almost nothing. The last desktop frame is kept on the GPU, so changing the region on a
/// still screen is answered without waiting for one.
///
/// The duplication is lost on a desktop switch (UAC, lock screen), a display mode change or a fullscreen
/// game taking over; it is then rebuilt with a growing back-off. A rotated display, a non-BGRA desktop or
/// a GPU that refuses duplication reports <see cref="GrabResult.Unsupported"/>, and the sampler moves to GDI.
/// </summary>
public sealed class DxgiScreenGrab : IScreenGrab
{
    private static readonly TimeSpan MinRetry = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(2);
    private static readonly FeatureLevel[] FeatureLevels = [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0];

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGIOutput1? _output;
    private IDXGIOutputDuplication? _duplication;
    private bool _freshDuplication;

    private ID3D11Texture2D? _desktop;          // copy of the latest desktop frame
    private ID3D11Texture2D? _mip;              // the region at full size, plus mips down to the read level
    private ID3D11ShaderResourceView? _mipView;
    private (int Width, int Height, int Levels) _mipShape;
    private ID3D11Texture2D? _staging;          // CPU-readable copy of the read level
    private (int Width, int Height) _stagingShape;
    private byte[] _readback = [];

    private bool _desktopChanged;
    private SampleRegion? _lastRegion;
    private long _retryAt;
    private TimeSpan _retryDelay = MinRetry;
    private bool _disposed;

    private DxgiScreenGrab()
    {
        // Fails (and the sampler uses GDI) when there is no D3D11 device or duplication is refused outright.
        Build();
    }

    /// <summary>A ready DXGI grab, or null when Desktop Duplication isn't available here.</summary>
    public static IScreenGrab? TryCreate()
    {
        try { return new DxgiScreenGrab(); }
        catch (Exception error) when (error is SharpGenException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }

    public GrabResult Grab(SampleRegion region, Span<byte> bgra)
    {
        if (_disposed) return GrabResult.Failed;
        try
        {
            if (_duplication is null)
            {
                if (Stopwatch.GetTimestamp() < _retryAt) return GrabResult.Failed;
                Build();
            }
            if (!TakeFrame()) return GrabResult.Failed;
            if (_desktop is null) return GrabResult.Failed;
            if (!_desktopChanged && region == _lastRegion) return GrabResult.Unchanged;

            Shrink(region, bgra);
            _desktopChanged = false;
            _lastRegion = region;
            return GrabResult.New;
        }
        catch (NotSupportedException)
        {
            return GrabResult.Unsupported;
        }
        catch (SharpGenException error) when (error.ResultCode == Vortice.DXGI.ResultCode.Unsupported)
        {
            return GrabResult.Unsupported;
        }
        catch (Exception error) when (error is SharpGenException or InvalidOperationException)
        {
            // Lost device, desktop switch, display change: rebuild everything after a back-off.
            TearDown();
            _retryAt = Stopwatch.GetTimestamp() + (long)(_retryDelay.TotalSeconds * Stopwatch.Frequency);
            _retryDelay = TimeSpan.FromTicks(Math.Min(_retryDelay.Ticks * 2, MaxRetry.Ticks));
            return GrabResult.Failed;
        }
    }

    /// <summary>Picks up a new desktop frame if there is one. False when the duplication was lost.</summary>
    private bool TakeFrame()
    {
        // A brand-new duplication may need a moment for its first frame; after that, never wait.
        var timeout = _freshDuplication ? 100u : 0u;
        var result = _duplication!.AcquireNextFrame(timeout, out var info, out var resource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout) return true; // nothing changed
        if (result == Vortice.DXGI.ResultCode.AccessLost) throw new InvalidOperationException("Desktop duplication was lost.");
        result.CheckError();

        _freshDuplication = false;
        try
        {
            // LastPresentTime 0 means only the pointer moved; the desktop image itself is the same.
            if (info.LastPresentTime != 0 || _desktop is null)
            {
                using var texture = resource.QueryInterface<ID3D11Texture2D>();
                var description = texture.Description;
                if (description.Format != Format.B8G8R8A8_UNorm) throw new NotSupportedException($"Desktop format {description.Format}.");
                EnsureDesktop(description);
                _context!.CopyResource(_desktop!, texture);
                _desktopChanged = true;
            }
        }
        finally
        {
            resource.Dispose();
            _duplication.ReleaseFrame();
        }
        _retryDelay = MinRetry;
        return true;
    }

    private void Shrink(SampleRegion region, Span<byte> bgra)
    {
        var context = _context!;
        var desktop = _desktop!.Description;
        var (x, y, w, h) = region.ToPixels((int)desktop.Width, (int)desktop.Height);
        var level = PixelResample.MipLevelFor(w, h, ScreenSampler.SampleWidth, ScreenSampler.SampleHeight);
        EnsureMip(w, h, level + 1);

        context.CopySubresourceRegion(_mip!, 0, 0, 0, 0, _desktop, 0, new Box(x, y, 0, x + w, y + h, 1));
        if (level > 0) context.GenerateMips(_mipView!);

        var levelWidth = Math.Max(1, w >> level);
        var levelHeight = Math.Max(1, h >> level);
        EnsureStaging(levelWidth, levelHeight);
        context.CopySubresourceRegion(_staging!, 0, 0, 0, 0, _mip!, (uint)level, null);

        var rowBytes = levelWidth * 4;
        var mapped = context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            for (var row = 0; row < levelHeight; row++)
                System.Runtime.InteropServices.Marshal.Copy(mapped.DataPointer + row * (nint)mapped.RowPitch, _readback, row * rowBytes, rowBytes);
        }
        finally { context.Unmap(_staging!, 0); }

        PixelResample.AreaAverage(_readback, levelWidth, levelHeight, rowBytes, bgra, ScreenSampler.SampleWidth, ScreenSampler.SampleHeight);
    }

    // ---- setup --------------------------------------------------------------

    /// <summary>Finds the primary monitor (the output at the desktop origin), makes a device on the GPU
    /// that drives it, and starts duplicating it.</summary>
    private void Build()
    {
        TearDown();
        try { BuildCore(); }
        catch { TearDown(); throw; } // leave nothing half-built behind
    }

    private void BuildCore()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint a = 0; factory.EnumAdapters1(a, out var adapter).Success; a++)
        {
            using (adapter)
            {
                for (uint o = 0; adapter.EnumOutputs(o, out var output).Success; o++)
                {
                    var description = output.Description;
                    if (!description.AttachedToDesktop || description.DesktopCoordinates.Left != 0 || description.DesktopCoordinates.Top != 0)
                    {
                        output.Dispose();
                        continue;
                    }
                    using (output)
                    {
                        if (description.Rotation is not (ModeRotation.Identity or ModeRotation.Unspecified))
                            throw new NotSupportedException("The primary display is rotated.");
                        D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, FeatureLevels,
                            out _device, out _context).CheckError();
                        _output = output.QueryInterface<IDXGIOutput1>();
                        _duplication = _output.DuplicateOutput(_device);
                        _freshDuplication = true;
                        _desktopChanged = true; // the desktop texture will be rebuilt from the first frame
                        return;
                    }
                }
            }
        }
        throw new InvalidOperationException("No primary display output was found.");
    }

    private void EnsureDesktop(Texture2DDescription source)
    {
        if (_desktop is { } existing)
        {
            var d = existing.Description;
            if (d.Width == source.Width && d.Height == source.Height) return;
            existing.Dispose();
            _lastRegion = null;
        }
        _desktop = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = source.Width,
            Height = source.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None,
        });
    }

    private void EnsureMip(int width, int height, int levels)
    {
        if (_mip is not null && _mipShape == (width, height, levels)) return;
        _mipView?.Dispose();
        _mip?.Dispose();
        _mip = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = (uint)levels,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget, // GenerateMips needs both
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.GenerateMips,
        });
        _mipView = _device.CreateShaderResourceView(_mip);
        _mipShape = (width, height, levels);
    }

    private void EnsureStaging(int width, int height)
    {
        if (_staging is not null && _stagingShape == (width, height)) return;
        _staging?.Dispose();
        _staging = _device!.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });
        _stagingShape = (width, height);
        if (_readback.Length < width * height * 4) _readback = new byte[width * height * 4];
    }

    private void TearDown()
    {
        _staging?.Dispose(); _staging = null;
        _mipView?.Dispose(); _mipView = null;
        _mip?.Dispose(); _mip = null;
        _desktop?.Dispose(); _desktop = null;
        _duplication?.Dispose(); _duplication = null;
        _output?.Dispose(); _output = null;
        _context?.Dispose(); _context = null;
        _device?.Dispose(); _device = null;
        _lastRegion = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TearDown();
    }
}

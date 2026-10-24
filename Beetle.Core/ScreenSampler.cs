using System.Runtime.InteropServices;

namespace Beetle.Core;

/// <summary>One representative colour for whatever is on screen right now.</summary>
public interface IScreenSampler : IDisposable
{
    RgbColor Sample();
}

/// <summary>
/// Grabs the chosen <see cref="Region"/> of the primary monitor through GDI and lets the graphics layer shrink it to a few hundred
/// pixels with a halftone (area-averaging) stretch. Reading back that tiny image and averaging it
/// is the whole-screen average colour, done cheaply enough to run many times a second.
///
/// GDI is used directly rather than System.Drawing so Core needs no extra package, and the
/// off-screen device context and bitmap are created once and reused for every frame.
/// </summary>
public sealed class GdiScreenSampler : IScreenSampler
{
    private const int SampleWidth = 64;
    private const int SampleHeight = 36;

    private readonly IntPtr _screenDc;
    private readonly IntPtr _memoryDc;
    private readonly IntPtr _bitmap;
    private readonly IntPtr _previousBitmap;
    private readonly byte[] _pixels = new byte[SampleWidth * SampleHeight * 4];
    private readonly Lock _regionLock = new();
    private SampleRegion _region = SampleRegion.Full;
    private bool _disposed;

    /// <summary>The part of the primary monitor to average. Safe to change while sampling runs.</summary>
    public SampleRegion Region
    {
        get { lock (_regionLock) return _region; }
        set { lock (_regionLock) _region = value.Normalize(); }
    }

    public GdiScreenSampler()
    {
        _screenDc = GetDC(IntPtr.Zero);
        _memoryDc = CreateCompatibleDC(_screenDc);
        _bitmap = CreateCompatibleBitmap(_screenDc, SampleWidth, SampleHeight);
        _previousBitmap = SelectObject(_memoryDc, _bitmap);
    }

    public RgbColor Sample()
    {
        if (_disposed) return RgbColor.Black;

        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);
        if (screenWidth <= 0 || screenHeight <= 0) return RgbColor.Black;

        // HALFTONE averages the source block behind each destination pixel, so the shrink itself
        // does most of the averaging work; SetBrushOrgEx is required after choosing that mode.
        var (srcX, srcY, srcW, srcH) = Region.ToPixels(screenWidth, screenHeight);
        SetStretchBltMode(_memoryDc, STRETCH_HALFTONE);
        SetBrushOrgEx(_memoryDc, 0, 0, IntPtr.Zero);
        if (!StretchBlt(_memoryDc, 0, 0, SampleWidth, SampleHeight, _screenDc, srcX, srcY, srcW, srcH, SRCCOPY))
            return RgbColor.Black;

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = SampleWidth,
            biHeight = -SampleHeight, // negative: top-down, so row order doesn't matter here
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };
        if (GetDIBits(_memoryDc, _bitmap, 0, SampleHeight, _pixels, ref header, DIB_RGB_COLORS) == 0)
            return RgbColor.Black;

        long r = 0, g = 0, b = 0;
        var count = SampleWidth * SampleHeight;
        for (var i = 0; i < _pixels.Length; i += 4)
        {
            b += _pixels[i];     // GDI packs pixels as BGRA
            g += _pixels[i + 1];
            r += _pixels[i + 2];
        }
        return new RgbColor((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SelectObject(_memoryDc, _previousBitmap);
        DeleteObject(_bitmap);
        DeleteDC(_memoryDc);
        ReleaseDC(IntPtr.Zero, _screenDc);
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int STRETCH_HALFTONE = 4;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint BI_RGB = 0;
    private const uint DIB_RGB_COLORS = 0;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int width, int height);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);
    [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr hDc, int mode);
    [DllImport("gdi32.dll")] private static extern bool SetBrushOrgEx(IntPtr hDc, int x, int y, IntPtr point);
    [DllImport("gdi32.dll")] private static extern bool StretchBlt(IntPtr dstDc, int dstX, int dstY, int dstW, int dstH, IntPtr srcDc, int srcX, int srcY, int srcW, int srcH, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr hDc, IntPtr bitmap, uint startScan, uint scanLines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }
}

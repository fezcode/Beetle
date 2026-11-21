using System.Runtime.InteropServices;

namespace Beetle.Core;

/// <summary>
/// The fallback grab, through GDI. GDI has no cheap way to shrink the screen: every grab reads the
/// whole source area back from the compositor, which is the expensive part (about 30 ms of CPU for a
/// full 4K screen). The shrink is done in two steps to keep the rest cheap: a point-sampling
/// (COLORONCOLOR) stretch to an intermediate four times the sample size, then an exact area average
/// down to 128x72 on the CPU. A one-step HALFTONE stretch averages properly too, but costs about twice
/// the CPU at 4K. Small coloured elements still survive: the intermediate keeps one pixel in every
/// 7-8 of a 4K screen.
///
/// The off-screen device context and bitmap are created once and reused for every frame.
/// </summary>
public sealed class GdiScreenGrab : IScreenGrab
{
    private const int Scale = 4;
    private const int MidWidth = ScreenSampler.SampleWidth * Scale;
    private const int MidHeight = ScreenSampler.SampleHeight * Scale;

    private readonly IntPtr _screenDc;
    private readonly IntPtr _memoryDc;
    private readonly IntPtr _bitmap;
    private readonly IntPtr _previousBitmap;
    private readonly byte[] _mid = new byte[MidWidth * MidHeight * 4];
    private bool _disposed;

    public GdiScreenGrab()
    {
        _screenDc = GetDC(IntPtr.Zero);
        _memoryDc = CreateCompatibleDC(_screenDc);
        _bitmap = CreateCompatibleBitmap(_screenDc, MidWidth, MidHeight);
        _previousBitmap = SelectObject(_memoryDc, _bitmap);
    }

    public GrabResult Grab(SampleRegion region, Span<byte> bgra)
    {
        if (_disposed) return GrabResult.Failed;

        var screenWidth = GetSystemMetrics(SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SM_CYSCREEN);
        if (screenWidth <= 0 || screenHeight <= 0) return GrabResult.Failed;

        // A region smaller than the intermediate is copied at its own size, not blown up.
        var (srcX, srcY, srcW, srcH) = region.ToPixels(screenWidth, screenHeight);
        var midW = Math.Min(MidWidth, srcW);
        var midH = Math.Min(MidHeight, srcH);
        SetStretchBltMode(_memoryDc, STRETCH_COLORONCOLOR);
        if (!StretchBlt(_memoryDc, 0, 0, midW, midH, _screenDc, srcX, srcY, srcW, srcH, SRCCOPY))
            return GrabResult.Failed;

        var header = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = MidWidth,
            biHeight = -MidHeight, // negative: top-down rows
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };
        if (GetDIBits(_memoryDc, _bitmap, 0, MidHeight, _mid, ref header, DIB_RGB_COLORS) == 0)
            return GrabResult.Failed;

        PixelResample.AreaAverage(_mid, midW, midH, MidWidth * 4, bgra, ScreenSampler.SampleWidth, ScreenSampler.SampleHeight);
        return GrabResult.New;
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
    private const int STRETCH_COLORONCOLOR = 3;
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

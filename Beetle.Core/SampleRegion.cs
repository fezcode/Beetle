namespace Beetle.Core;

/// <summary>
/// The part of the screen ambient mode averages, as fractions of the primary monitor (0-1) so it
/// survives resolution and DPI changes. <see cref="Full"/> is the whole screen.
/// </summary>
public readonly record struct SampleRegion(double X, double Y, double Width, double Height)
{
    /// <summary>Smallest side, as a fraction, so a stray click can't produce an empty area.</summary>
    public const double MinimumSize = 0.02;

    public static readonly SampleRegion Full = new(0, 0, 1, 1);

    /// <summary>The one-click choices offered next to "Pick area…".</summary>
    public static IReadOnlyList<(string Name, SampleRegion Region)> Presets { get; } =
    [
        ("Full screen", Full),
        ("Center", new(0.25, 0.25, 0.5, 0.5)),
        ("Top", new(0, 0, 1, 0.25)),
        ("Bottom", new(0, 0.75, 1, 0.25)),
        ("Left", new(0, 0, 0.25, 1)),
        ("Right", new(0.75, 0, 0.25, 1)),
    ];

    /// <summary>Clamps into the screen and enforces <see cref="MinimumSize"/>.</summary>
    public SampleRegion Normalize()
    {
        var w = Math.Clamp(Width, MinimumSize, 1);
        var h = Math.Clamp(Height, MinimumSize, 1);
        var x = Math.Clamp(X, 0, 1 - w);
        var y = Math.Clamp(Y, 0, 1 - h);
        return new SampleRegion(x, y, w, h);
    }

    /// <summary>Builds a region from two corner points given in any order, as fractions.</summary>
    public static SampleRegion FromCorners(double x1, double y1, double x2, double y2) =>
        new SampleRegion(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1)).Normalize();

    /// <summary>The region in pixels on a screen of the given size; always at least 1×1 and inside the screen.</summary>
    public (int X, int Y, int Width, int Height) ToPixels(int screenWidth, int screenHeight)
    {
        var r = Normalize();
        var x = (int)Math.Round(r.X * screenWidth);
        var y = (int)Math.Round(r.Y * screenHeight);
        var w = Math.Max(1, Math.Min(screenWidth - x, (int)Math.Round(r.Width * screenWidth)));
        var h = Math.Max(1, Math.Min(screenHeight - y, (int)Math.Round(r.Height * screenHeight)));
        return (x, y, w, h);
    }

    /// <summary>The preset this region matches, or null for a hand-picked area.</summary>
    public string? PresetName
    {
        get
        {
            var self = this; // a lambda inside a struct can't capture `this` directly
            return Presets.FirstOrDefault(p => Close(p.Region, self)).Name;
        }
    }

    private static bool Close(SampleRegion a, SampleRegion b) =>
        Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6 && Math.Abs(a.Width - b.Width) < 1e-6 && Math.Abs(a.Height - b.Height) < 1e-6;
}

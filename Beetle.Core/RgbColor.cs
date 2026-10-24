using System.Globalization;

namespace Beetle.Core;

/// <summary>
/// A plain 8-bit RGB colour. Beetle keeps its own tiny colour type rather than leaning on a UI
/// framework's, so the Core library has nothing to do with Avalonia or GDI and stays testable.
/// </summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static readonly RgbColor Black = new(0, 0, 0);
    public static readonly RgbColor White = new(255, 255, 255);

    /// <summary>Parses <c>#RRGGBB</c> or <c>RRGGBB</c>; returns false on anything else.</summary>
    public static bool TryParseHex(string? text, out RgbColor color)
    {
        color = Black;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var span = text.Trim().TrimStart('#');
        if (span.Length != 6) return false;
        if (!byte.TryParse(span.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return false;
        if (!byte.TryParse(span.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return false;
        if (!byte.TryParse(span.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
        color = new RgbColor(r, g, b);
        return true;
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Builds a colour from hue (0-360), saturation and value (both 0-1).</summary>
    public static RgbColor FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = value - c;
        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new RgbColor(Round((r + m) * 255), Round((g + m) * 255), Round((b + m) * 255));
    }

    /// <summary>Returns hue (0-360), saturation (0-1) and value (0-1).</summary>
    public (double Hue, double Saturation, double Value) ToHsv()
    {
        double r = R / 255d, g = G / 255d, b = B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        double hue = 0;
        if (delta > 0)
        {
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * (((b - r) / delta) + 2);
            else hue = 60 * (((r - g) / delta) + 4);
        }
        if (hue < 0) hue += 360;
        var saturation = max <= 0 ? 0 : delta / max;
        return (hue, saturation, max);
    }

    /// <summary>Pushes saturation up by <paramref name="factor"/>, leaving hue and value alone.
    /// A flat screen average tends toward grey; a gentle boost keeps the strip lively.</summary>
    public RgbColor Saturate(double factor)
    {
        var (h, s, v) = ToHsv();
        return FromHsv(h, s * factor, v);
    }

    /// <summary>
    /// Makes a colour punchier for an LED strip. <paramref name="amount"/> runs 0 (unchanged) to 1
    /// (strongest). Saturation rises, and brightness is lifted with a curve that raises dark values
    /// most, weighted by how colourful the colour already is: a dark red becomes a vivid red, while
    /// a dark grey stays dark instead of glowing a dull white. Black stays black.
    /// </summary>
    public RgbColor Boost(double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        if (amount <= 0) return this;
        var (h, s, v) = ToHsv();
        var saturation = Math.Min(1, s * (1 + 0.8 * amount));
        var value = Math.Pow(v, 1 - 0.6 * amount * s);
        return FromHsv(h, saturation, value);
    }

    /// <summary>Moves this colour a fraction of the way toward <paramref name="target"/> (0 = stay, 1 = jump).</summary>
    public RgbColor LerpTo(RgbColor target, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return new RgbColor(
            Round(R + (target.R - R) * amount),
            Round(G + (target.G - G) * amount),
            Round(B + (target.B - B) * amount));
    }

    /// <summary>Largest per-channel difference, 0-255. Used to decide whether a change is worth sending.</summary>
    public int MaxChannelDelta(RgbColor other) =>
        Math.Max(Math.Abs(R - other.R), Math.Max(Math.Abs(G - other.G), Math.Abs(B - other.B)));

    private static byte Round(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}

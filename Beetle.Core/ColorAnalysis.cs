namespace Beetle.Core;

/// <summary>How ambient mode turns a patch of screen into one colour.</summary>
public enum AmbientColorMode
{
    /// <summary>The average of every pixel in the sample area.</summary>
    Average,
    /// <summary>The average of only the coloured pixels: black, white and greys are set aside first.</summary>
    Selective,
}

/// <summary>
/// Reduces a block of 32-bit BGRA pixels (the layout GDI hands back) to one colour. Pure functions
/// over a span, so they are tested without a screen.
/// </summary>
public static class ColorAnalysis
{
    /// <summary>Saturation below this (0-1) counts as grey: white and every grey fall under it.</summary>
    public const double MinSaturation = 0.25;
    /// <summary>Brightness below this (0-1) counts as black, whatever the hue.</summary>
    public const double MinValue = 0.15;

    /// <summary>The plain average of every pixel; black if there are none.</summary>
    public static RgbColor Average(ReadOnlySpan<byte> bgra)
    {
        long r = 0, g = 0, b = 0;
        var count = bgra.Length / 4;
        if (count == 0) return RgbColor.Black;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            b += bgra[i];
            g += bgra[i + 1];
            r += bgra[i + 2];
        }
        return new RgbColor((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    /// <summary>
    /// The average of the coloured pixels only, or null when nothing coloured is left once black,
    /// white and greys are removed (a dark terminal, a white page). Callers decide what "nothing" means;
    /// ambient mode holds the last colour rather than dropping to grey.
    /// </summary>
    public static RgbColor? Selective(ReadOnlySpan<byte> bgra)
    {
        long r = 0, g = 0, b = 0, count = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte pb = bgra[i], pg = bgra[i + 1], pr = bgra[i + 2];
            if (!IsChromatic(pr, pg, pb)) continue;
            r += pr; g += pg; b += pb; count++;
        }
        if (count == 0) return null;
        return new RgbColor((byte)(r / count), (byte)(g / count), (byte)(b / count));
    }

    /// <summary>True for a pixel with real colour: bright enough not to be black, saturated enough not
    /// to be white or grey. Integer maths, because it runs for every sampled pixel many times a second.</summary>
    public static bool IsChromatic(byte r, byte g, byte b)
    {
        int max = Math.Max(r, Math.Max(g, b));
        int min = Math.Min(r, Math.Min(g, b));
        if (max < MinValue * 255) return false;                // black and near-black
        return (max - min) >= MinSaturation * max;              // saturation = (max - min) / max
    }

    /// <summary>Picks the reduction for a mode; null only from <see cref="Selective"/>.</summary>
    public static RgbColor? Reduce(ReadOnlySpan<byte> bgra, AmbientColorMode mode) =>
        mode == AmbientColorMode.Selective ? Selective(bgra) : Average(bgra);
}

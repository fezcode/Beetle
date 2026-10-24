namespace Beetle.Core;

/// <summary>
/// Builds the 9-byte frames an ELK-BLEDOM controller understands, written to GATT
/// characteristic <see cref="CharacteristicUuid"/> under service <see cref="ServiceUuid"/>.
///
/// Every frame is <c>7E .. .. .. .. .. .. .. EF</c>. These layouts come from community
/// reverse-engineering of the strip (the vendor publishes none); they are the widely
/// confirmed ELK-BLEDOM set and are kept here as the single source of truth.
/// </summary>
public static class LedCommands
{
    public static readonly Guid ServiceUuid = Guid.Parse("0000fff0-0000-1000-8000-00805f9b34fb");
    public static readonly Guid CharacteristicUuid = Guid.Parse("0000fff3-0000-1000-8000-00805f9b34fb");

    private const byte Header = 0x7E;
    private const byte Trailer = 0xEF;

    /// <summary>Power the strip on (<c>7E 00 04 F0 00 01 FF 00 EF</c>) or off.</summary>
    public static byte[] Power(bool on) => on
        ? [Header, 0x00, 0x04, 0xF0, 0x00, 0x01, 0xFF, 0x00, Trailer]
        : [Header, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0x00, Trailer];

    /// <summary>Set the whole strip to one colour (<c>7E 00 05 03 RR GG BB 00 EF</c>).</summary>
    public static byte[] Color(RgbColor color) =>
        [Header, 0x00, 0x05, 0x03, color.R, color.G, color.B, 0x00, Trailer];

    /// <summary>Set hardware brightness as a percentage, 0-100 (<c>7E 00 01 BB 00 00 00 00 EF</c>).</summary>
    public static byte[] Brightness(int percent)
    {
        var value = (byte)Math.Clamp(percent, 0, 100);
        return [Header, 0x00, 0x01, value, 0x00, 0x00, 0x00, 0x00, Trailer];
    }
}

namespace GoveeController.Domain.Devices;

/// <summary>
/// An RGB color value. Govee's API represents color as a single packed integer
/// (0-16777215, i.e. 0xRRGGBB); this type provides the conversion in both directions.
/// </summary>
/// <param name="R">Red channel, 0-255.</param>
/// <param name="G">Green channel, 0-255.</param>
/// <param name="B">Blue channel, 0-255.</param>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    /// <summary>Converts this color to Govee's packed 0xRRGGBB integer representation.</summary>
    public int ToPackedInt() => (R << 16) | (G << 8) | B;

    /// <summary>Builds an <see cref="RgbColor"/> from Govee's packed 0xRRGGBB integer representation.</summary>
    public static RgbColor FromPackedInt(int packed) => new(
        R: (byte)((packed >> 16) & 0xFF),
        G: (byte)((packed >> 8) & 0xFF),
        B: (byte)(packed & 0xFF));

    /// <summary>Formats the color as a "#RRGGBB" hex string, as used by HTML color inputs.</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

    /// <summary>Parses a "#RRGGBB" or "RRGGBB" hex string, as produced by HTML color inputs.</summary>
    public static RgbColor FromHex(string hex)
    {
        var span = hex.AsSpan().TrimStart('#');
        if (span.Length != 6)
        {
            throw new FormatException($"Expected a 6-digit hex color, got '{hex}'.");
        }

        return new RgbColor(
            R: byte.Parse(span[..2], System.Globalization.NumberStyles.HexNumber),
            G: byte.Parse(span[2..4], System.Globalization.NumberStyles.HexNumber),
            B: byte.Parse(span[4..6], System.Globalization.NumberStyles.HexNumber));
    }
}

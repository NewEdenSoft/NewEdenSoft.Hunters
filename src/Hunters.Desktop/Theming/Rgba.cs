using System.Globalization;

namespace NewEdenSoft.Hunters.Desktop.Theming;

/// <summary>An sRGB colour with straight alpha; components are 0–1.</summary>
public readonly record struct Rgba(double R, double G, double B, double A = 1)
{
    public static readonly Rgba Transparent = new(0, 0, 0, 0);

    /// <summary>Parses <c>#rrggbb</c> or <c>#rrggbbaa</c>.</summary>
    public static Rgba Parse(string hex)
    {
        var s = hex.StartsWith('#') ? hex[1..] : hex;
        if (s.Length is not (6 or 8))
            throw new FormatException($"Expected #rrggbb or #rrggbbaa, got '{hex}'.");

        double Byte(int i) => byte.Parse(s.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
        return new Rgba(Byte(0), Byte(2), Byte(4), s.Length == 8 ? Byte(6) : 1);
    }

    public byte R8 => ToByte(R);
    public byte G8 => ToByte(G);
    public byte B8 => ToByte(B);
    public byte A8 => ToByte(A);

    public string ToHex() => A8 == 255
        ? $"#{R8:x2}{G8:x2}{B8:x2}"
        : $"#{R8:x2}{G8:x2}{B8:x2}{A8:x2}";

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
}

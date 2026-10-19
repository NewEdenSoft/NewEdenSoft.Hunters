namespace NewEdenSoft.Hunters.Desktop.Theming;

/// <summary>
/// Colour mixing in the OKLab space, matching CSS <c>color-mix(in oklab, a p%, b)</c>:
/// premultiplied-alpha interpolation, so mixing with transparent keeps the hue of the other colour.
/// </summary>
public static class OkLab
{
    /// <summary>Mixes <paramref name="percentA"/>% of <paramref name="a"/> with the rest of <paramref name="b"/>.</summary>
    public static Rgba Mix(Rgba a, double percentA, Rgba b)
    {
        var t = Math.Clamp(percentA, 0, 100) / 100d;
        var (l1, a1, b1) = ToOkLab(a);
        var (l2, a2, b2) = ToOkLab(b);

        var alpha = a.A * t + b.A * (1 - t);
        if (alpha <= 0)
            return Rgba.Transparent;

        // Premultiply, interpolate, un-premultiply.
        double Lerp(double x, double y) => (x * a.A * t + y * b.A * (1 - t)) / alpha;
        var rgb = FromOkLab(Lerp(l1, l2), Lerp(a1, a2), Lerp(b1, b2));
        return rgb with { A = alpha };
    }

    public static (double L, double A, double B) ToOkLab(Rgba c)
    {
        var r = ToLinear(c.R);
        var g = ToLinear(c.G);
        var b = ToLinear(c.B);

        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);

        return (
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    public static Rgba FromOkLab(double lightness, double a, double b)
    {
        var l = Cube(lightness + 0.3963377774 * a + 0.2158037573 * b);
        var m = Cube(lightness - 0.1055613458 * a - 0.0638541728 * b);
        var s = Cube(lightness - 0.0894841775 * a - 1.2914855480 * b);

        return new Rgba(
            ToGamma(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
            ToGamma(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
            ToGamma(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    private static double Cube(double v) => v * v * v;

    private static double ToLinear(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double ToGamma(double c)
    {
        c = Math.Clamp(c, 0, 1);
        return c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
    }
}

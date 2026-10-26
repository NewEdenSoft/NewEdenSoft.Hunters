using System.Globalization;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>How a system's figures are written, like the web's <c>map/systemInfo.ts</c>.</summary>
public static class SystemInfo
{
    private static readonly (double Size, string Unit)[] Units = [(1e12, "T"), (1e9, "B"), (1e6, "M"), (1e3, "k")];

    /// <summary>Shortened like EVE players do: "950", "12.5k", "3.2M", "1.05B", "250M". Counts too.</summary>
    public static string FormatIsk(double isk)
    {
        foreach (var (size, unit) in Units)
        {
            if (isk < size)
                continue;
            var value = isk / size;
            var text = value >= 100
                ? Math.Round(value, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)
                : value >= 10
                    ? value.ToString("0.#", CultureInfo.InvariantCulture)
                    : value.ToString("0.##", CultureInfo.InvariantCulture);
            return text + unit;
        }

        return Math.Round(isk, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
    }
}

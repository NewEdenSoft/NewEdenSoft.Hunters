using System.Globalization;
using NewEdenSoft.Hunters.Contracts.Activity;
using NewEdenSoft.Hunters.Contracts.Hunting;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>How a system's figures are written, like the web's <c>map/systemInfo.ts</c>.</summary>
public static class SystemInfo
{
    private static readonly (double Size, string Unit)[] Units = [(1e12, "T"), (1e9, "B"), (1e6, "M"), (1e3, "k")];

    public const string PeakNow = "Peak now";

    /// <summary>The System widget's Last hour: NPC kills, ship kills, pod kills and jumps of the system at <paramref name="index"/> (map pack order).</summary>
    public static IReadOnlyList<MetricRow> MetricRows(ActivityMetrics metrics, int index) =>
    [
        Row("NPC kills", metrics.NpcKills, index),
        Row("Ship kills", metrics.ShipKills, index),
        Row("Pod kills", metrics.PodKills, index),
        Row("Jumps", metrics.ShipJumps, index),
    ];

    private static MetricRow Row(string label, ActivityColumns columns, int index)
    {
        var lastHour = columns.LastHour[index];
        return new MetricRow(
            label,
            lastHour,
            columns.PreviousHour is { } previous ? lastHour - previous[index] : null,
            columns.Day[index],
            columns.Typical?[index],
            PeakHint(columns.PeakRatio[index], columns.HoursToPeak[index]));
    }

    /// <summary>"Peak now" in the busiest hours (80% of the peak), "Peak in N h" for a peak within six hours, else null. As on the hover panel.</summary>
    public static string? PeakHint(double? ratio, int? hoursToPeak) =>
        ratio >= 0.8 ? PeakNow : hoursToPeak is > 0 and <= 6 ? $"Peak in {hoursToPeak} h" : null;

    public const string StatusPeakNow = "peak now";
    public const string StatusNow = "now";
    public const string StatusSeen = "seen";

    /// <summary>
    /// A profile seen in a system, at this hour of the day (UTC): "now" when it is usually seen there at this
    /// hour, "peak now" when this is its busiest hour there (80% of it), "seen" otherwise. As on the map.
    /// </summary>
    public static string ProfileStatus(double score, IReadOnlyList<double> hours, int hour)
    {
        if (HuntingLayer.LikelyNow(score, hours, hour) <= 0)
            return StatusSeen;
        var busiest = hours.DefaultIfEmpty(0).Max();
        return busiest > 0 && hours[hour] / busiest >= 0.8 ? StatusPeakNow : StatusNow;
    }

    /// <summary>The profiles likely now first (peak now, then now), then by score, as the server orders them.</summary>
    public static IReadOnlyList<HuntingPlaceProfile> SortProfiles(IEnumerable<HuntingPlaceProfile> profiles, int hour) =>
    [
        .. profiles
            .OrderBy(p => ProfileStatus(p.Score, p.Hours, hour) switch { StatusPeakNow => 0, StatusNow => 1, _ => 2 })
            .ThenByDescending(p => p.Score),
    ];

    /// <summary>How long ago, briefly: "now", "12m", "3h" (up to two days), "5d".</summary>
    public static string TimeAgo(DateTimeOffset time, DateTimeOffset now)
    {
        var minutes = (long)Math.Floor((now - time).TotalMinutes);
        if (minutes < 1)
            return "now";
        if (minutes < 60)
            return $"{minutes}m";
        var hours = minutes / 60;
        return hours < 48 ? $"{hours}h" : $"{hours / 24}d";
    }

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

/// <summary>One line of the System widget's Last hour.</summary>
/// <param name="Change">Against the hour before; null without one (downtime, a gap).</param>
/// <param name="Day">The last 24 hours.</param>
/// <param name="Typical">Usual for this hour of the week; null without data.</param>
/// <param name="Peak">"Peak now", "Peak in N h", or null.</param>
public sealed record MetricRow(string Label, int LastHour, int? Change, int Day, double? Typical, string? Peak)
{
    /// <summary>"▲ 40", "▼ 1", "±0", or "–" without an hour before.</summary>
    public string ChangeText => Change switch
    {
        null => "–",
        > 0 => $"▲ {Change}",
        < 0 => $"▼ {-Change}",
        _ => "±0",
    };

    public string ChangeTip => Change is null ? "No hour before to compare with" : "Since the hour before";

    public bool IsRising => Change > 0;

    public bool IsFalling => Change < 0;

    /// <summary>No change, or nothing to compare with: faint.</summary>
    public bool IsFlat => Change is null or 0;

    public string UsualText => Typical is { } typical ? Math.Round(typical, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture) : "–";

    /// <summary>The peak, set apart from the label; empty without one.</summary>
    public string PeakText => Peak is null ? "" : $"  {Peak}";

    public bool IsPeakNow => Peak == SystemInfo.PeakNow;
}

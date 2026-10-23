namespace NewEdenSoft.Hunters.Contracts.Activity;

/// <summary>
/// Activity now for every system and region on the map (docs/map-and-activity.md, "Serving activity"),
/// for colouring the whole map. Columnar: entry <c>i</c> of each system column belongs to
/// <c>MapPack.Systems[i]</c> of the map pack built from <see cref="SdeBuild"/>, and entry <c>i</c> of each
/// region column to <c>MapPack.Regions[i]</c>. Systems without activity have zeros.
/// </summary>
/// <param name="Version">The intel activity version; changes with every new ESI hour.</param>
/// <param name="KillsPeriodEnd">The end of the latest kills hour (ESI's <c>Last-Modified</c>).</param>
/// <param name="KillsDayHours">How many hours the kill <c>Day</c> sums cover (24 without gaps).</param>
public sealed record ActivityCurrent(
    long Version,
    long SdeBuild,
    DateTimeOffset KillsPeriodEnd,
    DateTimeOffset JumpsPeriodEnd,
    int KillsDayHours,
    int JumpsDayHours,
    ActivityMetrics Systems,
    ActivityMetrics Regions);

/// <summary>The four ESI metrics, as columns.</summary>
public sealed record ActivityMetrics(
    ActivityColumns NpcKills,
    ActivityColumns ShipKills,
    ActivityColumns PodKills,
    ActivityColumns ShipJumps);

/// <param name="LastHour">The latest hour.</param>
/// <param name="PreviousHour">The hour before it; null when that hour is missing (downtime, a gap), so there is no delta.</param>
/// <param name="Day">The last 24 hours.</param>
/// <param name="Typical">The average for the latest hour's hour of week ("usual for this hour"); null without samples yet.</param>
/// <param name="PeakRatio">
/// The hour now's average ÷ the busiest hour of the week's, 0–1 ("in its peak" from about 0.8). Null per entry
/// while there are too few weeks of data or the system is too quiet to have a peak.
/// </param>
/// <param name="HoursToPeak">Hours until the next peak hour, 0 = now, looking 24 hours ahead; null when none.</param>
/// <param name="PeakHourOfWeek">The busiest hour of the week (0 = Monday 00:00 UTC); null like <paramref name="PeakRatio"/>.</param>
public sealed record ActivityColumns(
    IReadOnlyList<int> LastHour,
    IReadOnlyList<int>? PreviousHour,
    IReadOnlyList<int> Day,
    IReadOnlyList<double>? Typical,
    IReadOnlyList<double?> PeakRatio,
    IReadOnlyList<int?> HoursToPeak,
    IReadOnlyList<int?> PeakHourOfWeek);

/// <summary>One system or region in detail: activity now and its week, for when it is selected.</summary>
/// <param name="Id">The system or region id.</param>
/// <param name="NowHourOfWeek">The hour of week after the latest kills hour ("now"), 0 = Monday 00:00 UTC.</param>
public sealed record ActivityDetails(
    int Id,
    long Version,
    DateTimeOffset KillsPeriodEnd,
    DateTimeOffset JumpsPeriodEnd,
    int KillsDayHours,
    int JumpsDayHours,
    int NowHourOfWeek,
    ActivityMetricDetails NpcKills,
    ActivityMetricDetails ShipKills,
    ActivityMetricDetails PodKills,
    ActivityMetricDetails ShipJumps);

/// <param name="Week">
/// The average per hour of week, 168 entries from Monday 00:00 UTC; null for hours without samples yet.
/// </param>
public sealed record ActivityMetricDetails(
    int LastHour,
    int? PreviousHour,
    int Day,
    double? Typical,
    double? PeakRatio,
    int? HoursToPeak,
    int? PeakHourOfWeek,
    IReadOnlyList<double?> Week);

public static class ActivityPaths
{
    /// <summary>Activity now for the whole map. Revalidate with the ETag.</summary>
    public const string Current = "/api/activity/current";

    public static string System(int systemId) => $"/api/activity/systems/{systemId}";

    public static string Region(int regionId) => $"/api/activity/regions/{regionId}";
}

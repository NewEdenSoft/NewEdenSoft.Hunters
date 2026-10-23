namespace NewEdenSoft.Hunters.Intel.Data;

/// <summary>
/// Hour-of-week sums for one system: everything ESI reported for this system in this hour of the
/// week, over the raw hours kept (90 days). Average = sum ÷ the bucket's samples in
/// <see cref="ActivityWeekSamples"/>. Only rows with activity exist; a missing row is zero.
/// </summary>
public sealed class SystemActivityWeek
{
    public int SystemId { get; set; }

    /// <summary>0 = Monday 00:00–01:00 UTC … 167 = Sunday 23:00–24:00 UTC.</summary>
    public short HourOfWeek { get; set; }

    public long NpcKills { get; set; }

    public long ShipKills { get; set; }

    public long PodKills { get; set; }

    public long ShipJumps { get; set; }
}

/// <summary>The same as <see cref="SystemActivityWeek"/>, summed over a region's systems.</summary>
public sealed class RegionActivityWeek
{
    public int RegionId { get; set; }

    public short HourOfWeek { get; set; }

    public long NpcKills { get; set; }

    public long ShipKills { get; set; }

    public long PodKills { get; set; }

    public long ShipJumps { get; set; }
}

/// <summary>How many raw hours each hour of the week holds, for kills and for jumps (their lists arrive separately).</summary>
public sealed class ActivityWeekSamples
{
    public short HourOfWeek { get; set; }

    public int KillHours { get; set; }

    public int JumpHours { get; set; }
}

/// <summary>The metrics, as property names of the current rows and columns of the week rows.</summary>
public static class ActivityMetrics
{
    public const string NpcKills = nameof(NpcKills), ShipKills = nameof(ShipKills), PodKills = nameof(PodKills), ShipJumps = nameof(ShipJumps);

    public static readonly string[] All = [NpcKills, ShipKills, PodKills, ShipJumps];
}

/// <summary>
/// One metric (NPC kills, ship kills, pod kills or ship jumps) for a system or region now. Stored as
/// columns of <see cref="SystemActivityCurrent"/> and <see cref="RegionActivityCurrent"/>.
/// </summary>
public sealed class ActivityMetric
{
    /// <summary>The latest hour.</summary>
    public int LastHour { get; set; }

    /// <summary>The hour before it; null when that hour is missing (the service was down, or ESI skipped it), so no delta is shown.</summary>
    public int? PreviousHour { get; set; }

    /// <summary>The raw hours ending in the last 24 hours, added up; see the current row's coverage for how many there were.</summary>
    public int Day { get; set; }

    /// <summary>The average for the latest hour's hour of week ("usual for this hour"); null without samples.</summary>
    public double? Typical { get; set; }

    /// <summary>
    /// The average for the hour now (the one after the latest data) ÷ the busiest hour of the week's
    /// average, 0–1. Null while there are too few samples or the system is too quiet to have a peak.
    /// </summary>
    public double? PeakRatio { get; set; }

    /// <summary>Hours until the next hour at or above the peak threshold (0 = now), looking 24 hours ahead; null when none.</summary>
    public short? HoursToPeak { get; set; }

    /// <summary>The busiest hour of the week; null like <see cref="PeakRatio"/>.</summary>
    public short? PeakHourOfWeek { get; set; }
}

/// <summary>
/// A system's activity now: the latest hour, the one before, the last 24 hours, and how they compare
/// with the system's usual week. Replaced on every new ESI hour (docs/map-and-activity.md).
/// </summary>
public sealed class SystemActivityCurrent
{
    public int SystemId { get; set; }

    /// <summary>The end of the latest kills hour (ESI's <c>Last-Modified</c>).</summary>
    public DateTimeOffset KillsPeriodEnd { get; set; }

    /// <summary>The end of the latest jumps hour.</summary>
    public DateTimeOffset JumpsPeriodEnd { get; set; }

    /// <summary>How many raw kills hours the 24-hour sums cover (24 without gaps).</summary>
    public short KillsDayHours { get; set; }

    public short JumpsDayHours { get; set; }

    public ActivityMetric NpcKills { get; set; } = new();

    public ActivityMetric ShipKills { get; set; } = new();

    public ActivityMetric PodKills { get; set; } = new();

    public ActivityMetric ShipJumps { get; set; } = new();
}

/// <summary>The same as <see cref="SystemActivityCurrent"/>, for a region.</summary>
public sealed class RegionActivityCurrent
{
    public int RegionId { get; set; }

    public DateTimeOffset KillsPeriodEnd { get; set; }

    public DateTimeOffset JumpsPeriodEnd { get; set; }

    public short KillsDayHours { get; set; }

    public short JumpsDayHours { get; set; }

    public ActivityMetric NpcKills { get; set; } = new();

    public ActivityMetric ShipKills { get; set; } = new();

    public ActivityMetric PodKills { get; set; } = new();

    public ActivityMetric ShipJumps { get; set; } = new();
}

/// <summary>
/// A version per intel dataset, bumped in the same transaction as every change to it, so readers (the
/// API server) know when to reload and never see a half-applied hour.
/// </summary>
public sealed class IntelVersion
{
    /// <summary>For example <see cref="Activity"/>.</summary>
    public string Dataset { get; set; } = "";

    public long Version { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public const string Activity = "activity";

    public const string Hunting = "hunting";
}

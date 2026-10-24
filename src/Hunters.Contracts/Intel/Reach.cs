namespace NewEdenSoft.Hunters.Contracts.Intel;

/// <summary>
/// The systems in reach of an origin with their intel (docs/map-and-activity.md, "Reach"): every system
/// within <see cref="Jumps"/> gate jumps and/or every cyno target within <see cref="Ly"/> light years.
/// Each row carries both distances, so the client decides what is in jump range for its ship.
/// </summary>
/// <param name="Profile">The profile the rows are sorted by ("likely now"); null sorts by <paramref name="Sort"/>.</param>
/// <param name="Total">How many systems are in reach; <see cref="Systems"/> holds at most the requested limit.</param>
/// <param name="HourOfDay">The current hour (UTC) "likely now" and "peak now" refer to.</param>
/// <param name="Sort">Without a profile, the order: <see cref="ReachSorts.NpcRising"/> or <see cref="ReachSorts.NpcKills"/>.</param>
public sealed record IntelReach(
    int From,
    int? Jumps,
    double? Ly,
    string? Profile,
    int Total,
    int HourOfDay,
    long? ActivityVersion,
    long? HuntingVersion,
    IReadOnlyList<ReachSystem> Systems,
    string Sort = ReachSorts.NpcRising);

/// <summary>The orders of <see cref="IntelReach"/> without a profile.</summary>
public static class ReachSorts
{
    /// <summary>The rise in NPC kills since the hour before (the default).</summary>
    public const string NpcRising = "npc-rising";

    /// <summary>The most NPC kills in the last hour, then in the last 24 hours.</summary>
    public const string NpcKills = "npc-kills";

    public static readonly IReadOnlyList<string> All = [NpcRising, NpcKills];
}

/// <param name="Jumps">Gate jumps from the origin; null when no gates lead there.</param>
/// <param name="Ly">Straight-line distance from the origin, in light years.</param>
/// <param name="Security">True security status, -1.0 to 1.0.</param>
/// <param name="CynoTarget">A jump drive can land here: not high-sec, Pochven or Zarzakh.</param>
/// <param name="Activity">ESI's hourly activity; null until the server has activity data.</param>
/// <param name="Profiles">The hunting profiles seen here, highest score first.</param>
public sealed record ReachSystem(
    int SystemId,
    int? Jumps,
    double Ly,
    double Security,
    bool CynoTarget,
    ReachActivity? Activity,
    IReadOnlyList<ReachProfile> Profiles);

public sealed record ReachActivity(ReachMetric NpcKills, ReachMetric ShipKills, ReachMetric PodKills, ReachMetric ShipJumps);

/// <param name="PreviousHour">Null when the hour before is missing, so there is no delta.</param>
/// <param name="Typical">Usual for this hour of the week; null without data.</param>
/// <param name="PeakRatio">The hour now against the busiest hour of the week, 0–1; null while there is too little data.</param>
/// <param name="HoursToPeak">Hours until the next peak hour, 0 = now; null when none in the next 24 hours.</param>
public sealed record ReachMetric(int LastHour, int? PreviousHour, int Day, double? Typical, double? PeakRatio, int? HoursToPeak);

/// <param name="LikelyNow">The score times this hour's share of the sightings: how likely the target is here now.</param>
/// <param name="PeakNow">This hour against the busiest hour of the day for the profile here, 0–1; null without sightings.</param>
public sealed record ReachProfile(string Profile, double Score, DateTimeOffset LastSeen, double LikelyNow, double? PeakNow);

public static class IntelPaths
{
    public static string Reach(int from, int? jumps = null, double? ly = null, string? profile = null, int? limit = null, string? sort = null)
    {
        var query = new List<string> { $"from={from}" };
        if (jumps is { } j)
            query.Add($"jumps={j}");
        if (ly is { } l)
            query.Add($"ly={l.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (profile is not null)
            query.Add($"profile={Uri.EscapeDataString(profile)}");
        if (limit is { } n)
            query.Add($"limit={n}");
        if (sort is not null)
            query.Add($"sort={Uri.EscapeDataString(sort)}");
        return $"/api/intel/reach?{string.Join('&', query)}";
    }
}

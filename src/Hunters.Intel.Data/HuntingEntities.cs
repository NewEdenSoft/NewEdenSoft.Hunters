namespace NewEdenSoft.Hunters.Intel.Data;

/// <summary>On a killmail, the victim died or the pilot was among the attackers.</summary>
public enum SightingRole : short
{
    Victim = 0,
    Attacker = 1,
}

/// <summary>
/// Evidence of a hunting profile's target in a system at a time (docs/map-and-activity.md, "Hunting
/// profiles"): a Carrier pilot on a killmail, a smartbomb fitted or fired, or indirect evidence such as a
/// CRAB beacon killed (<see cref="Indicator"/>, with a <see cref="Weight"/> below 1). One per profile,
/// pilot, system and hour, so one pilot ratting all evening counts once an hour, not once a kill.
/// </summary>
public sealed class Sighting
{
    public long Id { get; set; }

    /// <summary>The profile id, for example <c>carriers</c>.</summary>
    public string Profile { get; set; } = "";

    public long KillmailId { get; set; }

    public int SystemId { get; set; }

    /// <summary>The kill's time.</summary>
    public DateTimeOffset Time { get; set; }

    /// <summary><see cref="Time"/> to the hour, for the one-per-hour rule.</summary>
    public DateTimeOffset Hour { get; set; }

    /// <summary>The character seen; 0 when there is none (a CRAB beacon, a ship without a pilot).</summary>
    public long PilotId { get; set; }

    public SightingRole Role { get; set; }

    /// <summary>What matched: the ship, the smart bomb fitted or fired, or the beacon.</summary>
    public int TypeId { get; set; }

    /// <summary>How much it counts: 1 for direct evidence, less for an indicator.</summary>
    public double Weight { get; set; }

    /// <summary>Indirect evidence (a CRAB beacon for Carriers) rather than the target itself on the killmail.</summary>
    public bool Indicator { get; set; }

    /// <summary>
    /// Part of a large fight (many attackers, or a burst of kills in the system), so not typical of the
    /// place: kept, but not counted.
    /// </summary>
    public bool IsFleet { get; set; }

    /// <summary>The kill's attackers, for the fleet rule.</summary>
    public int AttackerCount { get; set; }
}

/// <summary>
/// The hunting profiles in use, and the definition each one's sightings were built with; a change
/// backfills them from the stored kills. Readers (the API server) list the profiles from here.
/// </summary>
public sealed class HuntingProfileState
{
    public string Profile { get; set; } = "";

    /// <summary>The display name, for example "Carriers".</summary>
    public string Name { get; set; } = "";

    /// <summary>The profile's rules with the SDE ids they resolved to.</summary>
    public string Definition { get; set; } = "";

    public DateTimeOffset BackfilledAt { get; set; }
}

/// <summary>
/// An admin's request to rebuild hunting profiles' sightings from the stored kills, as when a definition
/// changes. The API server adds it (its one write to the <c>intel</c> schema); the intel service picks
/// it up, rebuilds, and marks it done.
/// </summary>
public sealed class HuntingRebuildRequest
{
    public long Id { get; set; }

    /// <summary>The profile ids to rebuild; empty for all of them.</summary>
    public string[] Profiles { get; set; } = [];

    /// <summary>The admin's account.</summary>
    public Guid RequestedBy { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>When the intel service took it up; null while waiting.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When it finished, rebuilt or failed; null until then.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Why it failed; null when it didn't.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Where a hunting profile's target is found (docs/map-and-activity.md, "Hunting aggregates"): its
/// sightings in a system, fleet fights left out, indicators at their weight. Recomputed from the
/// sightings when they change and hourly, so the windows and the decay stay current.
/// </summary>
public sealed class HuntingSystemStats
{
    public string Profile { get; set; } = "";

    public int SystemId { get; set; }

    /// <summary>Weighted sightings in the last 7 days.</summary>
    public double Week { get; set; }

    /// <summary>Weighted sightings in the last 30 days.</summary>
    public double Month { get; set; }

    /// <summary>The part of <see cref="Month"/> from indicators (a CRAB beacon for Carriers).</summary>
    public double MonthIndicator { get; set; }

    /// <summary>
    /// All weighted sightings, each fading with a half-life (14 days): recent habits count most, and a
    /// system nobody has used for weeks drifts towards zero.
    /// </summary>
    public double Score { get; set; }

    /// <summary>The latest sighting that counts (not a fleet fight).</summary>
    public DateTimeOffset LastSeen { get; set; }

    /// <summary>Different pilots seen in the last 30 days.</summary>
    public int Pilots { get; set; }

    /// <summary>Weighted sightings per hour of the day (UTC), 24 entries, over all kept sightings: when it is there.</summary>
    public double[] Hours { get; set; } = [];
}

/// <summary>The same as <see cref="HuntingSystemStats"/>, for a region's systems together.</summary>
public sealed class HuntingRegionStats
{
    public string Profile { get; set; } = "";

    public int RegionId { get; set; }

    public double Week { get; set; }

    public double Month { get; set; }

    public double MonthIndicator { get; set; }

    public double Score { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    public int Pilots { get; set; }

    public double[] Hours { get; set; } = [];
}

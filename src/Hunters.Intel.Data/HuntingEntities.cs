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

/// <summary>The definition each profile's sightings were built with; a change backfills them from the stored kills.</summary>
public sealed class HuntingProfileState
{
    public string Profile { get; set; } = "";

    /// <summary>The profile's rules with the SDE ids they resolved to.</summary>
    public string Definition { get; set; } = "";

    public DateTimeOffset BackfilledAt { get; set; }
}

namespace NewEdenSoft.Hunters.Intel.Data;

/// <summary>
/// One kill from zKillboard's R2Z2 feed, slimmed to what intel needs (docs/map-and-activity.md, "Kills"):
/// where and when, the victim with its fitted modules, and every attacker's pilot, corporation,
/// alliance, ship and weapon as parallel arrays (hunting profiles count victims per ship, and
/// smartbombers by an area weapon hitting their own corporation or alliance). About 35,000 a
/// day, kept for <c>Hunters:Kills:Retention</c>.
/// </summary>
public sealed class Kill
{
    public long KillmailId { get; set; }

    /// <summary>With the id, fetches the full killmail from ESI (and links to zKillboard).</summary>
    public string Hash { get; set; } = "";

    /// <summary>The R2Z2 sequence number it arrived with.</summary>
    public long Sequence { get; set; }

    /// <summary>When the ship died (the killmail's time, not when zKillboard got it).</summary>
    public DateTimeOffset Time { get; set; }

    public int SystemId { get; set; }

    /// <summary>zKillboard: the victim died to NPCs only.</summary>
    public bool Npc { get; set; }

    /// <summary>zKillboard: one player on the killmail.</summary>
    public bool Solo { get; set; }

    /// <summary>zKillboard's estimate of what was destroyed and dropped, in ISK.</summary>
    public double TotalValue { get; set; }

    public long? VictimCharacterId { get; set; }

    public long? VictimCorporationId { get; set; }

    public long? VictimAllianceId { get; set; }

    /// <summary>The ship, or the structure or deployable (a CRAB beacon, for example), that died.</summary>
    public int VictimShipTypeId { get; set; }

    /// <summary>The distinct types fitted in its low, mid and high slots, rigs and subsystems; cargo is left out.</summary>
    public int[] VictimFittedTypeIds { get; set; } = [];

    /// <summary>The number of attackers (the arrays below have one entry each).</summary>
    public int AttackerCount { get; set; }

    /// <summary>Per attacker; 0 for NPCs and structures without a pilot.</summary>
    public long[] AttackerCharacterIds { get; set; } = [];

    /// <summary>
    /// Per attacker; 0 when unknown. Empty for kills stored before attackers' corporations were kept, until
    /// they are filled in from ESI (the corporation the pilot is in then, not at the kill).
    /// </summary>
    public long[] AttackerCorporationIds { get; set; } = [];

    /// <summary>Per attacker; 0 for none. Empty like <see cref="AttackerCorporationIds"/>.</summary>
    public long[] AttackerAllianceIds { get; set; } = [];

    /// <summary>Per attacker; 0 when unknown.</summary>
    public int[] AttackerShipTypeIds { get; set; } = [];

    /// <summary>Per attacker, the weapon of its last hit; 0 when unknown.</summary>
    public int[] AttackerWeaponTypeIds { get; set; } = [];

    /// <summary>
    /// The attacker (an index into the arrays above) that dealt the final blow; -1 when unknown, as for
    /// kills stored before it was kept.
    /// </summary>
    public int FinalBlow { get; set; } = -1;
}

/// <summary>How far a feed has been read, so the service resumes where it stopped.</summary>
public sealed class FeedPosition
{
    /// <summary>For example <see cref="R2Z2"/>.</summary>
    public string Feed { get; set; } = "";

    /// <summary>The last sequence number handled.</summary>
    public long Sequence { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public const string R2Z2 = "r2z2";
}

namespace NewEdenSoft.Hunters.Contracts.Kills;

/// <summary>
/// A system's most recent kills from zKillboard's feed, newest first (docs/map-and-activity.md, "Recent
/// kills"). Ids only: clients look the names up (ESI <c>POST /universe/names</c>) and draw the images.
/// </summary>
public sealed record SystemKills(int SystemId, IReadOnlyList<RecentKill> Kills);

/// <param name="Hash">With the id, the killmail on ESI; zKillboard links need only the id.</param>
/// <param name="Npc">The victim died to NPCs only.</param>
/// <param name="Solo">One player on the killmail.</param>
/// <param name="TotalValue">zKillboard's estimate of what was destroyed and dropped, in ISK.</param>
/// <param name="FinalBlow">Who dealt the final blow; null when unknown (kills stored before it was kept).</param>
/// <param name="AttackerCount">Everyone on the killmail, NPCs included.</param>
public sealed record RecentKill(
    long KillmailId,
    string Hash,
    DateTimeOffset Time,
    bool Npc,
    bool Solo,
    double TotalValue,
    KillParty Victim,
    KillParty? FinalBlow,
    int AttackerCount);

/// <summary>A pilot (or an NPC or structure without one) and its ship; ids are null when there are none.</summary>
/// <param name="ShipTypeId">The ship, structure or deployable; null when unknown (an attacker without a ship).</param>
public sealed record KillParty(long? CharacterId, long? CorporationId, long? AllianceId, int? ShipTypeId);

public static class KillPaths
{
    public const int DefaultLimit = 20, MaxLimit = 50;

    public static string System(int systemId, int? limit = null) =>
        limit is { } n ? $"/api/kills/systems/{systemId}?limit={n}" : $"/api/kills/systems/{systemId}";
}

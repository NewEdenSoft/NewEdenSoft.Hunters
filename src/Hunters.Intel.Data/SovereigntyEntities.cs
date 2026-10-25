namespace NewEdenSoft.Hunters.Intel.Data;

/// <summary>
/// An alliance's sovereignty claim on a system, from ESI's <c>/sovereignty/systems</c>
/// (docs/map-and-activity.md, "Sovereignty"). Systems held by an NPC faction, or by nobody, have no
/// row. The whole table is replaced hourly.
/// </summary>
public sealed class SystemSovereignty
{
    public int SystemId { get; set; }

    public long AllianceId { get; set; }

    /// <summary>The alliance's corporation that holds the claim.</summary>
    public long CorporationId { get; set; }

    /// <summary>Since when the alliance holds it; null when ESI doesn't say.</summary>
    public DateTimeOffset? ClaimedSince { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

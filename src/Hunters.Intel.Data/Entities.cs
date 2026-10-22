namespace NewEdenSoft.Hunters.Intel.Data;

/// <summary>
/// ESI's ship, pod and NPC kills per solar system in the hour ending at <see cref="PeriodEnd"/> (its
/// <c>Last-Modified</c>). One row per hour, the systems as parallel arrays: about 2,800 systems an
/// hour would otherwise be 2,800 rows. Only systems with kills are listed. This is the raw input the
/// derived activity tables are built from.
/// </summary>
public sealed class SystemKillsSnapshot
{
    public DateTimeOffset PeriodEnd { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public int[] SystemIds { get; set; } = [];

    public int[] ShipKills { get; set; } = [];

    public int[] PodKills { get; set; } = [];

    public int[] NpcKills { get; set; } = [];
}

/// <summary>ESI's ship jumps per solar system in the hour ending at <see cref="PeriodEnd"/>, as parallel arrays.</summary>
public sealed class SystemJumpsSnapshot
{
    public DateTimeOffset PeriodEnd { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public int[] SystemIds { get; set; } = [];

    public int[] ShipJumps { get; set; } = [];
}

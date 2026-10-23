namespace NewEdenSoft.Hunters.Domain.Map;

/// <summary>
/// Where a jump drive can land: a cynosural field can be lit there (docs/map-and-activity.md, "Reach").
/// Hulls, skills and fuel are the player's business; this is only about the system.
/// </summary>
public static class CynoTargets
{
    /// <summary>Solar system ids from here on are wormhole space, Abyssal deadspace and other instanced space.</summary>
    public const int FirstNonKnownSpaceSystemId = 31_000_000;

    /// <summary>Pochven: Triglavian space, no cynosural fields.</summary>
    public const int Pochven = 10000070;

    /// <summary>Zarzakh (in Yasna Zakh): no cynosural fields.</summary>
    public const int Zarzakh = 30100000;

    /// <summary>
    /// Not high-sec (cynos can't be lit at a shown 0.5 or more), not wormhole or other instanced space,
    /// not Pochven, not Zarzakh.
    /// </summary>
    public static bool IsTarget(int systemId, int regionId, double security) =>
        systemId < FirstNonKnownSpaceSystemId
        && regionId != Pochven
        && systemId != Zarzakh
        && !SecurityStatus.IsHighSec(security);
}

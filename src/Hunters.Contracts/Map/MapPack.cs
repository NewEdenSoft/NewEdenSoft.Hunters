namespace NewEdenSoft.Hunters.Contracts.Map;

/// <summary>
/// The map of known space that clients draw and route on, built from one SDE build. Clients cache
/// it by <see cref="SdeBuild"/>. See docs/map-and-activity.md, "Map pack".
/// </summary>
/// <remarks>
/// Positions are in light years and oriented for screens: <c>X</c> grows to the east (right) and
/// <c>Y</c> to the south (down), so clients can draw them without flipping an axis.
/// </remarks>
public sealed record MapPack(
    long SdeBuild,
    IReadOnlyList<MapRegion> Regions,
    IReadOnlyList<MapSystem> Systems,
    IReadOnlyList<MapGate> Gates);

public sealed record MapRegion(int Id, string Name);

/// <param name="Security">True security status, -1.0 to 1.0, unrounded.</param>
/// <param name="TopDown">The 3D position seen from above (SDE <c>x</c> and <c>z</c>), like the in-game star map.</param>
/// <param name="Flat">CCP's own flattened layout (SDE <c>position2D</c>), like the in-game 2D map.</param>
public sealed record MapSystem(
    int Id,
    string Name,
    int RegionId,
    int ConstellationId,
    double Security,
    MapPoint TopDown,
    MapPoint Flat);

public readonly record struct MapPoint(double X, double Y);

/// <summary>A stargate connection, listed once per pair with <see cref="From"/> &lt; <see cref="To"/>.</summary>
public sealed record MapGate(int From, int To);

public static class MapPaths
{
    /// <summary>The map pack of one SDE build. Immutable: cache it by build number.</summary>
    public static string Pack(long sdeBuild) => $"/api/map/packs/{sdeBuild}";
}

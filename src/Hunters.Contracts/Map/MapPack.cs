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
    IReadOnlyList<MapGate> Gates)
{
    /// <summary>
    /// The pack's format, in its URL (<see cref="MapPaths.Pack"/>) and the discovery document. Packs are
    /// cached as immutable, so a new format must be a new URL. 2 added <see cref="MapSystem.Height"/>.
    /// </summary>
    public const int Format = 2;
}

public sealed record MapRegion(int Id, string Name);

/// <param name="Security">True security status, -1.0 to 1.0, unrounded.</param>
/// <param name="TopDown">The 3D position seen from above (SDE <c>x</c> and <c>z</c>), like the in-game star map.</param>
/// <param name="Flat">
/// CCP's own flattened layout (SDE <c>position2D</c>), like the in-game 2D map. For display only: it is not
/// to scale, so never measure distances on it (use <paramref name="TopDown"/> and <paramref name="Height"/>).
/// </param>
/// <param name="Height">
/// The third axis of the 3D position (SDE <c>y</c>), in light years: with <paramref name="TopDown"/>, the
/// straight-line distance between two systems, as jump drives measure it.
/// </param>
public sealed record MapSystem(
    int Id,
    string Name,
    int RegionId,
    int ConstellationId,
    double Security,
    MapPoint TopDown,
    MapPoint Flat,
    double Height = 0);

public readonly record struct MapPoint(double X, double Y);

/// <summary>A stargate connection, listed once per pair with <see cref="From"/> &lt; <see cref="To"/>.</summary>
public sealed record MapGate(int From, int To);

public static class MapPaths
{
    /// <summary>The map pack of one SDE build, in a format. Immutable: cache it by this URL.</summary>
    public static string Pack(long sdeBuild, int format = MapPack.Format) => $"/api/map/packs/{sdeBuild}?format={format}";
}

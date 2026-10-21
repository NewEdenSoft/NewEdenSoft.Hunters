namespace NewEdenSoft.Hunters.Server.Infrastructure.Sde;

/// <summary>The SDE map data the server needs, in plain records so the map pack can be built and tested without the SDE.</summary>
public sealed record SdeMapData(
    long Build,
    IReadOnlyList<SdeRegion> Regions,
    IReadOnlyList<SdeSolarSystem> Systems,
    IReadOnlyList<SdeStargate> Stargates);

public sealed record SdeRegion(int Id, string Name);

/// <param name="X">SDE position, metres.</param>
/// <param name="Z">SDE position, metres.</param>
/// <param name="FlatX">SDE <c>position2D</c>, metres; null when the SDE has none (wormhole space).</param>
public sealed record SdeSolarSystem(
    int Id,
    string Name,
    int RegionId,
    int ConstellationId,
    double Security,
    double X,
    double Z,
    double? FlatX,
    double? FlatY);

public sealed record SdeStargate(int SolarSystemId, int DestinationSolarSystemId);

/// <summary>Loads the latest SDE map data, downloading a new build when there is one.</summary>
public interface ISdeMapSource
{
    Task<SdeMapData> LoadAsync(CancellationToken cancellationToken);
}

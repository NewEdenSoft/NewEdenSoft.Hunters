namespace NewEdenSoft.Hunters.Sde;

/// <summary>The SDE map data, in plain records so the map pack and the intel catalogue can be built and tested without the SDE.</summary>
public sealed record SdeMapData(
    long Build,
    IReadOnlyList<SdeRegion> Regions,
    IReadOnlyList<SdeSolarSystem> Systems,
    IReadOnlyList<SdeStargate> Stargates);

public sealed record SdeRegion(int Id, string Name);

/// <param name="X">SDE position, metres.</param>
/// <param name="Y">SDE position, metres: up and down. The 2D layouts leave it out; distances need it.</param>
/// <param name="Z">SDE position, metres.</param>
/// <param name="FlatX">SDE <c>position2D</c>, metres; null when the SDE has none (wormhole space).</param>
public sealed record SdeSolarSystem(
    int Id,
    string Name,
    int RegionId,
    int ConstellationId,
    double Security,
    double X,
    double Y,
    double Z,
    double? FlatX,
    double? FlatY);

public sealed record SdeStargate(int SolarSystemId, int DestinationSolarSystemId);

/// <summary>Item types with their groups and categories, for matching ships and modules on killmails.</summary>
public sealed record SdeTypeData(
    long Build,
    IReadOnlyList<SdeType> Types,
    IReadOnlyList<SdeGroup> Groups,
    IReadOnlyList<SdeCategory> Categories);

public sealed record SdeType(int Id, string Name, int GroupId, bool Published);

public sealed record SdeGroup(int Id, string Name, int CategoryId);

public sealed record SdeCategory(int Id, string Name);

/// <summary>Loads the latest SDE map data, downloading a new build when there is one.</summary>
public interface ISdeMapSource
{
    Task<SdeMapData> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>Loads the latest SDE item types, groups and categories (about 50,000 types).</summary>
public interface ISdeTypeSource
{
    Task<SdeTypeData> LoadAsync(CancellationToken cancellationToken);
}

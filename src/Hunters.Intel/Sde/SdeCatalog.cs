using System.Collections.Frozen;
using NewEdenSoft.Hunters.Sde;

namespace NewEdenSoft.Hunters.Intel.Sde;

/// <summary>A solar system as intel needs it. The position is in light years, in 3D (for jump-drive distances).</summary>
public sealed record CatalogSystem(int Id, string Name, int RegionId, int ConstellationId, double Security, double X, double Y, double Z)
{
    public double DistanceLy(CatalogSystem other) =>
        Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y) + (Z - other.Z) * (Z - other.Z));
}

/// <summary>An item type with its group and category, for matching ships, modules and deployables on killmails.</summary>
public sealed record CatalogType(int Id, string Name, int GroupId, int CategoryId);

/// <summary>
/// The SDE data the intel service works with: systems (with their regions and 3D positions) for region
/// curves and distances, and item types for hunting profiles. Built from one SDE build; immutable, with
/// frozen dictionaries because it is read for every system of every hour and every killmail.
/// </summary>
public sealed class SdeCatalog
{
    private readonly FrozenDictionary<string, CatalogType> _typesByName;

    private SdeCatalog(SdeMapData map, SdeTypeData types)
    {
        MapBuild = map.Build;
        TypeBuild = types.Build;
        Systems = map.Systems.ToFrozenDictionary(
            s => s.Id,
            s => new CatalogSystem(s.Id, s.Name, s.RegionId, s.ConstellationId, s.Security,
                s.X / SdeUnits.MetresPerLightYear, s.Y / SdeUnits.MetresPerLightYear, s.Z / SdeUnits.MetresPerLightYear));
        Regions = map.Regions.ToFrozenDictionary(r => r.Id, r => r.Name);

        var categoryOfGroup = types.Groups.ToDictionary(g => g.Id, g => g.CategoryId);
        Groups = types.Groups.ToFrozenDictionary(g => g.Id);
        Types = types.Types.ToFrozenDictionary(t => t.Id, t => new CatalogType(t.Id, t.Name, t.GroupId, categoryOfGroup.GetValueOrDefault(t.GroupId)));

        // Published types win when names repeat (old and test copies of a type keep their names).
        _typesByName = types.Types
            .OrderByDescending(t => t.Published)
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(g => g.Key, g => Types[g.First().Id], StringComparer.OrdinalIgnoreCase);
    }

    public long MapBuild { get; }

    public long TypeBuild { get; }

    public FrozenDictionary<int, CatalogSystem> Systems { get; }

    public FrozenDictionary<int, string> Regions { get; }

    public FrozenDictionary<int, CatalogType> Types { get; }

    public FrozenDictionary<int, SdeGroup> Groups { get; }

    public static SdeCatalog Build(SdeMapData map, SdeTypeData types) => new(map, types);

    /// <summary>A type by its English name, ignoring case; for profiles that name a type (the CRAB beacon).</summary>
    public CatalogType? FindType(string name) => _typesByName.GetValueOrDefault(name);
}

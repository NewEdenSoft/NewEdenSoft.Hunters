using NewEdenSoft.Hunters.Contracts.Map;
using NewEdenSoft.Hunters.Domain.Map;
using NewEdenSoft.Hunters.Sde;

namespace NewEdenSoft.Hunters.Server.Features.Map;

/// <summary>Builds the map pack of known space from SDE map data.</summary>
public static class MapPackBuilder
{
    public const double MetresPerLightYear = SdeUnits.MetresPerLightYear;

    /// <summary>Solar system ids from here on are wormhole space, Abyssal deadspace and other instanced space.</summary>
    public const int FirstNonKnownSpaceSystemId = 31_000_000;

    /// <summary>
    /// Known space is every k-space system (id below <see cref="FirstNonKnownSpaceSystemId"/>) with at
    /// least one stargate. That leaves out the unreachable Jove systems and keeps Pochven and Zarzakh.
    /// </summary>
    public static MapPack Build(SdeMapData sde)
    {
        var gated = sde.Stargates.Select(g => g.SolarSystemId).ToHashSet();
        var systems = sde.Systems
            .Where(s => s.Id < FirstNonKnownSpaceSystemId && gated.Contains(s.Id))
            .OrderBy(s => s.Id)
            .ToList();
        var systemIds = systems.Select(s => s.Id).ToHashSet();

        var gates = sde.Stargates
            .Where(g => systemIds.Contains(g.SolarSystemId) && systemIds.Contains(g.DestinationSolarSystemId))
            .Select(g => new MapGate(
                Math.Min(g.SolarSystemId, g.DestinationSolarSystemId),
                Math.Max(g.SolarSystemId, g.DestinationSolarSystemId)))
            .Distinct()
            .OrderBy(g => g.From).ThenBy(g => g.To)
            .ToList();

        var regionIds = systems.Select(s => s.RegionId).ToHashSet();
        var regions = sde.Regions
            .Where(r => regionIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .Select(r => new MapRegion(r.Id, r.Name))
            .ToList();

        return new MapPack(sde.Build, regions, [.. systems.Select(ToMapSystem)], gates);
    }

    /// <summary>The pack's systems and gates in 3D (SDE positions in light years), for range searches.</summary>
    public static MapGeometry Geometry(SdeMapData sde, MapPack pack)
    {
        var inPack = pack.Systems.Select(s => s.Id).ToHashSet();
        return MapGeometry.Build(
            sde.Systems.Where(s => inPack.Contains(s.Id)).Select(s => new GeometrySystem(
                s.Id, s.RegionId, s.Security, s.X / MetresPerLightYear, s.Y / MetresPerLightYear, s.Z / MetresPerLightYear)),
            pack.Gates.Select(g => (g.From, g.To)));
    }

    private static MapSystem ToMapSystem(SdeSolarSystem s)
    {
        // Screen orientation: SDE z (and position2D y) grow to the north, screens grow downwards.
        var topDown = Point(s.X, -s.Z);
        var flat = s is { FlatX: { } fx, FlatY: { } fy } ? Point(fx, -fy) : topDown;
        return new MapSystem(s.Id, s.Name, s.RegionId, s.ConstellationId, Math.Round(s.Security, 4), topDown, flat);
    }

    private static MapPoint Point(double xMetres, double yMetres) =>
        new(Math.Round(xMetres / MetresPerLightYear, 3), Math.Round(yMetres / MetresPerLightYear, 3));
}

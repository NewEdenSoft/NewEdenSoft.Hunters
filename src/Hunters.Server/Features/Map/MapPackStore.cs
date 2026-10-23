using NewEdenSoft.Hunters.Contracts.Map;
using NewEdenSoft.Hunters.Domain.Map;
using NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;

namespace NewEdenSoft.Hunters.Server.Features.Map;

/// <summary>
/// A map pack with its JSON, precompressed once so every download is just a copy of bytes, and the same
/// systems as <see cref="MapGeometry"/> (3D positions, the gate graph) for the server's range searches.
/// </summary>
public sealed class MapPackSnapshot
{
    private MapPackSnapshot(MapPack pack, PrecompressedJson body, MapGeometry geometry)
    {
        Pack = pack;
        Body = body;
        Geometry = geometry;
    }

    public MapPack Pack { get; }

    public long Build => Pack.SdeBuild;

    public PrecompressedJson Body { get; }

    public MapGeometry Geometry { get; }

    public static MapPackSnapshot Create(MapPack pack, MapGeometry geometry) => new(pack, PrecompressedJson.Create(pack), geometry);
}

/// <summary>The current map pack, held in memory. Null until the first SDE load finishes.</summary>
public sealed class MapPackStore
{
    private volatile MapPackSnapshot? _current;

    public MapPackSnapshot? Current => _current;

    public MapPackSnapshot Publish(MapPack pack, MapGeometry geometry)
    {
        var snapshot = MapPackSnapshot.Create(pack, geometry);
        _current = snapshot;
        return snapshot;
    }
}

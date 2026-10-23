using NewEdenSoft.Hunters.Contracts.Map;
using NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;

namespace NewEdenSoft.Hunters.Server.Features.Map;

/// <summary>A map pack with its JSON, precompressed once so every download is just a copy of bytes.</summary>
public sealed class MapPackSnapshot
{
    private MapPackSnapshot(MapPack pack, PrecompressedJson body)
    {
        Pack = pack;
        Body = body;
    }

    public MapPack Pack { get; }

    public long Build => Pack.SdeBuild;

    public PrecompressedJson Body { get; }

    public static MapPackSnapshot Create(MapPack pack) => new(pack, PrecompressedJson.Create(pack));
}

/// <summary>The current map pack, held in memory. Null until the first SDE load finishes.</summary>
public sealed class MapPackStore
{
    private volatile MapPackSnapshot? _current;

    public MapPackSnapshot? Current => _current;

    public MapPackSnapshot Publish(MapPack pack)
    {
        var snapshot = MapPackSnapshot.Create(pack);
        _current = snapshot;
        return snapshot;
    }
}

using System.IO.Compression;
using System.Text.Json;
using NewEdenSoft.Hunters.Contracts.Map;

namespace NewEdenSoft.Hunters.Server.Features.Map;

/// <summary>A map pack with its JSON, precompressed once so every download is just a copy of bytes.</summary>
public sealed class MapPackSnapshot
{
    private MapPackSnapshot(MapPack pack, byte[] json, byte[] brotli, byte[] gzip)
    {
        Pack = pack;
        Json = json;
        Brotli = brotli;
        Gzip = gzip;
    }

    public MapPack Pack { get; }

    public long Build => Pack.SdeBuild;

    public byte[] Json { get; }

    public byte[] Brotli { get; }

    public byte[] Gzip { get; }

    public static MapPackSnapshot Create(MapPack pack)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(pack, JsonSerializerOptions.Web);
        return new MapPackSnapshot(pack, json,
            Compress(json, s => new BrotliStream(s, CompressionLevel.SmallestSize)),
            Compress(json, s => new GZipStream(s, CompressionLevel.SmallestSize)));
    }

    private static byte[] Compress(byte[] data, Func<Stream, Stream> compressor)
    {
        using var output = new MemoryStream();
        using (var stream = compressor(output))
            stream.Write(data);
        return output.ToArray();
    }
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

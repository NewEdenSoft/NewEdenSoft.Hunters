using System.IO.Compression;
using System.Text.Json;
using Microsoft.Net.Http.Headers;

namespace NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;

/// <summary>
/// A JSON response that is the same bytes for everyone, serialised and compressed once so every
/// download is just a copy (the map pack, activity for the whole map).
/// </summary>
public sealed class PrecompressedJson
{
    private PrecompressedJson(byte[] json, byte[] brotli, byte[] gzip)
    {
        Json = json;
        Brotli = brotli;
        Gzip = gzip;
    }

    public byte[] Json { get; }

    public byte[] Brotli { get; }

    public byte[] Gzip { get; }

    public static PrecompressedJson Create<T>(T value)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.Web);
        return new PrecompressedJson(json,
            Compress(json, s => new BrotliStream(s, CompressionLevel.SmallestSize)),
            Compress(json, s => new GZipStream(s, CompressionLevel.SmallestSize)));
    }

    /// <summary>The best encoding the request accepts, with <c>Vary</c> and <c>Content-Encoding</c> set on the response.</summary>
    public IResult ToResult(HttpContext http)
    {
        var accepted = http.Request.GetTypedHeaders().AcceptEncoding
            .Where(e => e.Quality is null or > 0)
            .Select(e => e.Value.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var (bytes, encoding) = accepted.Contains("br") ? (Brotli, "br")
            : accepted.Contains("gzip") ? (Gzip, "gzip")
            : (Json, (string?)null);

        http.Response.Headers.Vary = HeaderNames.AcceptEncoding;
        if (encoding is not null)
            http.Response.Headers.ContentEncoding = encoding;
        return Results.Bytes(bytes, "application/json");
    }

    private static byte[] Compress(byte[] data, Func<Stream, Stream> compressor)
    {
        using var output = new MemoryStream();
        using (var stream = compressor(output))
            stream.Write(data);
        return output.ToArray();
    }
}

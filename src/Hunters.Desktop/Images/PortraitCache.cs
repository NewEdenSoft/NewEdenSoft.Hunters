using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using NewEdenSoft.Hunters.Client;

namespace NewEdenSoft.Hunters.Desktop.Images;

/// <summary>
/// Character portraits and type icons from EVE's image server, kept in memory for the life of the app so
/// lists that rebuild do not download them again. A failed download is not cached and is retried the
/// next time the image is asked for.
/// </summary>
public sealed class PortraitCache(IHttpClientFactory httpClients, ILogger<PortraitCache> logger)
{
    public const string HttpClientName = "EveImages";

    private readonly ConcurrentDictionary<Uri, Task<Bitmap?>> _images = new();

    /// <summary>The portrait, or null when it could not be downloaded (offline, image server down).</summary>
    public Task<Bitmap?> GetCharacterPortraitAsync(long characterId, int size = 64) => Get(EveImages.CharacterPortrait(characterId, size));

    /// <summary>A ship's (or any type's) icon at 32 or 64 pixels, or null when it could not be downloaded.</summary>
    public Task<Bitmap?> GetTypeIconAsync(int typeId, int size = 64) => Get(EveImages.TypeIcon(typeId, size));

    private Task<Bitmap?> Get(Uri url) => _images.GetOrAdd(url, LoadAsync);

    private async Task<Bitmap?> LoadAsync(Uri url)
    {
        try
        {
            var bytes = await httpClients.CreateClient(HttpClientName).GetByteArrayAsync(url);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException)
        {
            logger.LogDebug(e, "Could not load the image {Url}", url);
            _images.TryRemove(url, out _);
            return null;
        }
    }
}

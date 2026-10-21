using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using NewEdenSoft.Hunters.Client;

namespace NewEdenSoft.Hunters.Desktop.Images;

/// <summary>
/// Character portraits from EVE's image server, kept in memory for the life of the app so lists
/// that rebuild do not download them again. A failed download is not cached and is retried the
/// next time the portrait is asked for.
/// </summary>
public sealed class PortraitCache(IHttpClientFactory httpClients, ILogger<PortraitCache> logger)
{
    public const string HttpClientName = "EveImages";

    private readonly ConcurrentDictionary<(long CharacterId, int Size), Task<Bitmap?>> _portraits = new();

    /// <summary>The portrait, or null when it could not be downloaded (offline, image server down).</summary>
    public Task<Bitmap?> GetCharacterPortraitAsync(long characterId, int size = 64) =>
        _portraits.GetOrAdd((characterId, size), key => LoadAsync(key.CharacterId, key.Size));

    private async Task<Bitmap?> LoadAsync(long characterId, int size)
    {
        try
        {
            var bytes = await httpClients.CreateClient(HttpClientName).GetByteArrayAsync(EveImages.CharacterPortrait(characterId, size));
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException)
        {
            logger.LogDebug(e, "Could not load the portrait of character {CharacterId}", characterId);
            _portraits.TryRemove((characterId, size), out _);
            return null;
        }
    }
}

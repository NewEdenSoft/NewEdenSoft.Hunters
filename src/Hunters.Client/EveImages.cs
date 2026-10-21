namespace NewEdenSoft.Hunters.Client;

/// <summary>
/// URLs on EVE's public image server (no authentication).
/// See https://developers.eveonline.com/docs/services/image-server/.
/// </summary>
public static class EveImages
{
    public static readonly Uri BaseUrl = new("https://images.evetech.net/");

    /// <summary>Sizes the image server supports, in pixels.</summary>
    public static readonly IReadOnlyList<int> Sizes = [32, 64, 128, 256, 512, 1024];

    public static Uri CharacterPortrait(long characterId, int size = 64) =>
        new(BaseUrl, $"characters/{characterId}/portrait?size={CheckSize(size)}");

    private static int CheckSize(int size) =>
        Sizes.Contains(size) ? size : throw new ArgumentOutOfRangeException(nameof(size), size, "Use one of 32, 64, 128, 256, 512 or 1024.");
}

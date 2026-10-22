namespace NewEdenSoft.Hunters.Sde;

/// <summary>Where and how the SDE is downloaded. Both the API server and the intel service use it.</summary>
public sealed class SdeOptions
{
    /// <summary>
    /// Directory for the downloaded SDE archive (about 100 MB) and the extracted datasets. Keep it on a
    /// volume to avoid downloading again after a restart. Defaults to a folder under local application data.
    /// </summary>
    public string? CachePath { get; set; }

    /// <summary>How CCP can reach the operator, sent in the User-Agent.</summary>
    public string Contact { get; set; } = "https://github.com/NewEdenSoft/NewEdenSoft.Hunters";

    public string ResolvedCachePath => CachePath
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewEdenSoft", "Hunters", "sde");
}

public static class SdeUnits
{
    /// <summary>SDE positions are in metres.</summary>
    public const double MetresPerLightYear = 9_460_730_472_580_800;
}

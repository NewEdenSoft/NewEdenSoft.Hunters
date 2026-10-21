using Microsoft.Extensions.Options;
using NewEdenKit.Sde;
using NewEdenSoft.Hunters.Server.Configuration;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Sde;

/// <summary>
/// Map data from CCP's SDE through NewEdenKit.Sde. The archive (about 100 MB) is downloaded only
/// when CCP publishes a new build; the map datasets are kept as small files next to it.
/// </summary>
public sealed class SdeMapSource(IHttpClientFactory httpClients, IOptions<HuntersOptions> options, IConfiguration configuration, ILogger<SdeMapSource> logger)
    : ISdeMapSource
{
    public const string HttpClientName = "Sde";

    public async Task<SdeMapData> LoadAsync(CancellationToken cancellationToken)
    {
        var contact = configuration["Hunters:Esi:Contact"] ?? "https://github.com/NewEdenSoft/NewEdenSoft.Hunters";
        using var client = new SdeClient(httpClients.CreateClient(HttpClientName), new SdeClientOptions
        {
            UserAgent = $"NewEdenSoft.Hunters (+{contact})",
        });
        var root = options.Value.Sde.CachePath
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NewEdenSoft", "Hunters", "sde");
        var loader = new SdeLoader(client, new SdeLoaderOptions { CacheDirectory = Path.Combine(root, "archives") });
        var store = new SdeFileStore(Path.Combine(root, "store"));

        var result = await loader.LoadAsync(store, new SdeLoadOptions
        {
            Datasets = [SdeDatasets.Regions, SdeDatasets.SolarSystems, SdeDatasets.Stargates],
        }, cancellationToken);
        if (!result.UpToDate)
            logger.LogInformation("Loaded SDE build {Build} map data", result.Build.BuildNumber);

        var regions = await store.ReadListAsync(SdeDatasets.Regions, cancellationToken);
        var systems = await store.ReadListAsync(SdeDatasets.SolarSystems, cancellationToken);
        var stargates = await store.ReadListAsync(SdeDatasets.Stargates, cancellationToken);

        return new SdeMapData(
            result.Build.BuildNumber,
            [.. regions.Select(r => new SdeRegion(r.Id, r.Name.ToString()))],
            [.. systems.Select(s => new SdeSolarSystem(
                s.Id, s.Name.ToString(), s.RegionId, s.ConstellationId, s.SecurityStatus,
                s.Position.X, s.Position.Z, s.Position2D?.X, s.Position2D?.Y))],
            [.. stargates.Select(g => new SdeStargate(g.SolarSystemId, g.Destination.SolarSystemId))]);
    }
}

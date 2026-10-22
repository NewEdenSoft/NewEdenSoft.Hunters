using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NewEdenKit.Sde;

namespace NewEdenSoft.Hunters.Sde;

/// <summary>
/// The SDE through NewEdenKit.Sde. The archive (about 100 MB) is downloaded only when CCP publishes a
/// new build; the datasets each caller needs are kept as small files next to it.
/// </summary>
public sealed class SdeSource(IHttpClientFactory httpClients, IOptions<SdeOptions> options, ILogger<SdeSource> logger)
    : ISdeMapSource, ISdeTypeSource
{
    public const string HttpClientName = "Sde";

    async Task<SdeMapData> ISdeMapSource.LoadAsync(CancellationToken cancellationToken)
    {
        var (build, store) = await LoadAsync([SdeDatasets.Regions, SdeDatasets.SolarSystems, SdeDatasets.Stargates], cancellationToken);
        var regions = await store.ReadListAsync(SdeDatasets.Regions, cancellationToken);
        var systems = await store.ReadListAsync(SdeDatasets.SolarSystems, cancellationToken);
        var stargates = await store.ReadListAsync(SdeDatasets.Stargates, cancellationToken);

        return new SdeMapData(
            build,
            [.. regions.Select(r => new SdeRegion(r.Id, r.Name.ToString()))],
            [.. systems.Select(s => new SdeSolarSystem(
                s.Id, s.Name.ToString(), s.RegionId, s.ConstellationId, s.SecurityStatus,
                s.Position.X, s.Position.Y, s.Position.Z, s.Position2D?.X, s.Position2D?.Y))],
            [.. stargates.Select(g => new SdeStargate(g.SolarSystemId, g.Destination.SolarSystemId))]);
    }

    async Task<SdeTypeData> ISdeTypeSource.LoadAsync(CancellationToken cancellationToken)
    {
        var (build, store) = await LoadAsync([SdeDatasets.Types, SdeDatasets.Groups, SdeDatasets.Categories], cancellationToken);
        var types = await store.ReadListAsync(SdeDatasets.Types, cancellationToken);
        var groups = await store.ReadListAsync(SdeDatasets.Groups, cancellationToken);
        var categories = await store.ReadListAsync(SdeDatasets.Categories, cancellationToken);

        return new SdeTypeData(
            build,
            [.. types.Select(t => new SdeType(t.Id, t.Name.ToString(), t.GroupId, t.Published))],
            [.. groups.Select(g => new SdeGroup(g.Id, g.Name.ToString(), g.CategoryId))],
            [.. categories.Select(c => new SdeCategory(c.Id, c.Name.ToString()))]);
    }

    private async Task<(long Build, SdeFileStore Store)> LoadAsync(IReadOnlyList<SdeDataset> datasets, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var client = new SdeClient(httpClients.CreateClient(HttpClientName), new SdeClientOptions
        {
            UserAgent = $"NewEdenSoft.Hunters (+{settings.Contact})",
        });
        var root = settings.ResolvedCachePath;
        var loader = new SdeLoader(client, new SdeLoaderOptions { CacheDirectory = Path.Combine(root, "archives") });
        var store = new SdeFileStore(Path.Combine(root, "store"));

        var result = await loader.LoadAsync(store, new SdeLoadOptions { Datasets = [.. datasets] }, cancellationToken);
        if (!result.UpToDate)
            logger.LogInformation("Loaded SDE build {Build}: {Datasets}", result.Build.BuildNumber, string.Join(", ", datasets.Select(d => d.Name)));
        return (result.Build.BuildNumber, store);
    }
}

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
        return new SdeMapData(
            build,
            await SlimAsync(store, SdeDatasets.Regions, r => new SdeRegion(r.Id, r.Name.ToString()), cancellationToken),
            await SlimAsync(store, SdeDatasets.SolarSystems, s => new SdeSolarSystem(
                s.Id, s.Name.ToString(), s.RegionId, s.ConstellationId, s.SecurityStatus,
                s.Position.X, s.Position.Y, s.Position.Z, s.Position2D?.X, s.Position2D?.Y), cancellationToken),
            await SlimAsync(store, SdeDatasets.Stargates, g => new SdeStargate(g.SolarSystemId, g.Destination.SolarSystemId), cancellationToken));
    }

    async Task<SdeTypeData> ISdeTypeSource.LoadAsync(CancellationToken cancellationToken)
    {
        var (build, store) = await LoadAsync([SdeDatasets.Types, SdeDatasets.Groups, SdeDatasets.Categories], cancellationToken);
        return new SdeTypeData(
            build,
            await SlimAsync(store, SdeDatasets.Types, t => new SdeType(t.Id, t.Name.ToString(), t.GroupId, t.Published), cancellationToken),
            await SlimAsync(store, SdeDatasets.Groups, g => new SdeGroup(g.Id, g.Name.ToString(), g.CategoryId), cancellationToken),
            await SlimAsync(store, SdeDatasets.Categories, c => new SdeCategory(c.Id, c.Name.ToString()), cancellationToken));
    }

    /// <summary>
    /// Streams a dataset and keeps only what Hunters uses of each record. Reading whole datasets into
    /// lists (<c>ReadListAsync</c>) held every record with all its languages and descriptions: about
    /// 290 MB for the item types, which stayed reachable after the load.
    /// </summary>
    private static async Task<List<TSlim>> SlimAsync<TRecord, TSlim>(
        SdeFileStore store, SdeDataset<TRecord> dataset, Func<TRecord, TSlim> slim, CancellationToken cancellationToken)
    {
        var records = new List<TSlim>();
        await foreach (var record in store.ReadAsync(dataset, cancellationToken))
            records.Add(slim(record));
        return records;
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

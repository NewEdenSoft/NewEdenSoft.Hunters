using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Server.Configuration;
using NewEdenSoft.Hunters.Sde;

namespace NewEdenSoft.Hunters.Server.Features.Map;

/// <summary>
/// Builds the map pack at startup and rebuilds it when CCP publishes a new SDE build. Failures
/// (CCP unreachable) are retried with a growing delay; until the first load succeeds the map
/// endpoints answer 503.
/// </summary>
public sealed class MapPackService(
    ISdeMapSource source,
    MapPackStore store,
    IOptions<HuntersOptions> options,
    TimeProvider time,
    ILogger<MapPackService> logger) : BackgroundService
{
    private static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var refresh = options.Value.Sde.RefreshInterval;
            TimeSpan delay;
            try
            {
                var sde = await source.LoadAsync(stoppingToken);
                if (store.Current?.Build != sde.Build)
                {
                    var pack = MapPackBuilder.Build(sde);
                    var snapshot = store.Publish(pack, MapPackBuilder.Geometry(sde, pack));
                    logger.LogInformation(
                        "Map pack for SDE build {Build}: {Systems} systems, {Gates} gates, {Size} KB compressed",
                        snapshot.Build, snapshot.Pack.Systems.Count, snapshot.Pack.Gates.Count, snapshot.Body.Brotli.Length / 1024);
                }

                failures = 0;
                delay = refresh;
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                failures++;
                delay = TimeSpan.FromTicks(Math.Min(FirstRetry.Ticks << Math.Min(failures - 1, 8), refresh.Ticks));
                logger.LogWarning(e, "Could not load the SDE map data (attempt {Attempt}); retrying in {Delay}", failures, delay);
            }

            await Task.Delay(delay, time, stoppingToken);
        }
    }
}

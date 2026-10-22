using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Sde;

namespace NewEdenSoft.Hunters.Intel.Sde;

/// <summary>The current <see cref="SdeCatalog"/>, once the SDE has loaded.</summary>
public sealed class SdeCatalogStore
{
    private readonly TaskCompletionSource<SdeCatalog> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile SdeCatalog? _current;

    public SdeCatalog? Current => _current;

    /// <summary>Completes when the first catalogue is published, for work that cannot start without it.</summary>
    public Task<SdeCatalog> WhenReadyAsync(CancellationToken cancellationToken) => _first.Task.WaitAsync(cancellationToken);

    public void Publish(SdeCatalog catalog)
    {
        _current = catalog;
        _first.TrySetResult(catalog);
    }
}

/// <summary>
/// Loads the SDE map and item types at startup and again when CCP publishes a new build. Failures (CCP
/// unreachable) are retried with a growing delay; until the first load, the catalogue is unavailable.
/// </summary>
public sealed partial class SdeCatalogService(
    ISdeMapSource maps,
    ISdeTypeSource types,
    SdeCatalogStore store,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<SdeCatalogService> logger) : BackgroundService
{
    public static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(1);

    private int _failures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await LoadAsync(stoppingToken);
            await Task.Delay(delay, time, stoppingToken);
        }
    }

    /// <summary>Loads the SDE and publishes a new catalogue when the build changed. Returns how long to wait before the next check.</summary>
    public async Task<TimeSpan> LoadAsync(CancellationToken cancellationToken)
    {
        var refresh = options.Value.Sde.RefreshInterval;
        try
        {
            var map = await maps.LoadAsync(cancellationToken);
            var typeData = await types.LoadAsync(cancellationToken);
            if (store.Current is not { } current || current.MapBuild != map.Build || current.TypeBuild != typeData.Build)
            {
                var catalog = SdeCatalog.Build(map, typeData);
                store.Publish(catalog);
                LogLoaded(catalog.MapBuild, catalog.Systems.Count, catalog.Regions.Count, catalog.Types.Count);
            }

            _failures = 0;
            return refresh;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _failures++;
            var delay = TimeSpan.FromTicks(Math.Min(FirstRetry.Ticks << Math.Min(_failures - 1, 8), refresh.Ticks));
            LogFailed(e, _failures, delay);
            return delay;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SDE catalogue for build {Build}: {Systems} systems, {Regions} regions, {Types} types")]
    private partial void LogLoaded(long build, int systems, int regions, int types);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the SDE (attempt {Attempt}); retrying in {Delay}")]
    private partial void LogFailed(Exception exception, int attempt, TimeSpan delay);
}

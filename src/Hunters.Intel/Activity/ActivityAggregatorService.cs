using Microsoft.Extensions.Options;

namespace NewEdenSoft.Hunters.Intel.Activity;

/// <summary>
/// Runs <see cref="ActivityAggregator.UpdateAsync"/> at startup, whenever the collector stores a new
/// hour (<see cref="Signal"/>), and at least hourly so old hours expire even while ESI is unreachable.
/// Failures are retried with the collector's backoff.
/// </summary>
public sealed partial class ActivityAggregatorService(
    ActivityAggregator aggregator,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<ActivityAggregatorService> logger) : BackgroundService
{
    /// <summary>The longest wait without a signal.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _signal = new(0, 1);
    private int _failures;

    /// <summary>Tells the service new hours are stored. Signals while it is busy collapse into one more run.</summary>
    public void Signal()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another signal got there first; one run covers both.
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Activity.Enabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await RunAsync(stoppingToken);
            using var timeout = new CancellationTokenSource(delay, time);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
            try
            {
                await _signal.WaitAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // The interval passed without a signal.
            }
        }
    }

    /// <summary>One update. Returns how long to wait at most before the next.</summary>
    public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await aggregator.UpdateAsync(cancellationToken);
            _failures = 0;
            return Interval;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _failures++;
            var delay = SystemActivityService.Backoff(_failures);
            LogFailed(e, _failures, delay);
            return delay;
        }
    }

    public override void Dispose()
    {
        _signal.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not update the activity tables (attempt {Attempt}); retrying in {Delay}")]
    private partial void LogFailed(Exception exception, int attempt, TimeSpan delay);
}

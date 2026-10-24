using Microsoft.Extensions.Options;

namespace NewEdenSoft.Hunters.Intel.Kills;

/// <summary>
/// Spaces every request to R2Z2 at least <c>1 / Hunters:Kills:MaxRequestsPerSecond</c> apart, whoever
/// makes it. zKillboard blocks an address for an hour above 15 requests a second, so this is a hard cap
/// for the whole process, not a pace the reader keeps by itself.
/// </summary>
public sealed class KillFeedGate(IOptions<IntelOptions> options, TimeProvider time)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long? _last;

    public TimeSpan Interval => TimeSpan.FromSeconds(1 / Math.Max(options.Value.Kills.MaxRequestsPerSecond, 0.1));

    /// <summary>Waits until the next request may go.</summary>
    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_last is { } last && Interval - time.GetElapsedTime(last) is var wait && wait > TimeSpan.Zero)
                await Task.Delay(wait, time, cancellationToken);
            _last = time.GetTimestamp();
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>zKillboard refused the request for too many (429 or 403): the feed must pause for <see cref="RetryAfter"/>.</summary>
public sealed class KillFeedBlockedException(TimeSpan retryAfter, int status)
    : HttpRequestException($"R2Z2 answered {status}: blocked for too many requests; pausing for {retryAfter}.")
{
    public TimeSpan RetryAfter => retryAfter;
}

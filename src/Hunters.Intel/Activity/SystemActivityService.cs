using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel.Data;

namespace NewEdenSoft.Hunters.Intel.Activity;

/// <summary>
/// Collects ESI's hourly kills and jumps per solar system for everyone (docs/map-and-activity.md,
/// "Hourly kills and jumps"). It fetches both at startup and again when ESI's copies expire, stores
/// each hour once as a raw snapshot, and signals the <see cref="ActivityAggregatorService"/>, which
/// adds new hours to the derived tables and expires old ones. Failures are retried with a growing delay.
/// </summary>
public sealed partial class SystemActivityService(
    ISystemActivitySource source,
    IServiceScopeFactory scopes,
    ActivityAggregatorService aggregator,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<SystemActivityService> logger) : BackgroundService
{
    /// <summary>When ESI gives no expiry, ask again after this long.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(10);

    /// <summary>Waited after ESI's expiry, so its caches have the new hour; never less than a minute in all.</summary>
    public static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    private int _failures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Activity.Enabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await PollAsync(stoppingToken);
            await Task.Delay(delay, time, stoppingToken);
        }
    }

    /// <summary>Fetches both lists and stores new hours. Returns how long to wait before the next poll.</summary>
    public async Task<TimeSpan> PollAsync(CancellationToken cancellationToken)
    {
        try
        {
            var kills = await source.GetKillsAsync(cancellationToken);
            var jumps = await source.GetJumpsAsync(cancellationToken);
            var now = time.GetUtcNow();

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();

            var stored = false;
            if (!await db.SystemKills.AnyAsync(s => s.PeriodEnd == kills.PeriodEnd, cancellationToken))
            {
                db.SystemKills.Add(new SystemKillsSnapshot
                {
                    PeriodEnd = kills.PeriodEnd,
                    FetchedAt = now,
                    SystemIds = [.. kills.Systems.Select(s => s.SystemId)],
                    ShipKills = [.. kills.Systems.Select(s => s.ShipKills)],
                    PodKills = [.. kills.Systems.Select(s => s.PodKills)],
                    NpcKills = [.. kills.Systems.Select(s => s.NpcKills)],
                });
                LogStored("kills", kills.PeriodEnd, kills.Systems.Count);
                stored = true;
            }

            if (!await db.SystemJumps.AnyAsync(s => s.PeriodEnd == jumps.PeriodEnd, cancellationToken))
            {
                db.SystemJumps.Add(new SystemJumpsSnapshot
                {
                    PeriodEnd = jumps.PeriodEnd,
                    FetchedAt = now,
                    SystemIds = [.. jumps.Systems.Select(s => s.SystemId)],
                    ShipJumps = [.. jumps.Systems.Select(s => s.ShipJumps)],
                });
                LogStored("jumps", jumps.PeriodEnd, jumps.Systems.Count);
                stored = true;
            }

            await db.SaveChangesAsync(cancellationToken);
            if (stored)
                aggregator.Signal();

            _failures = 0;
            return NextPoll(now, kills.Expires, jumps.Expires);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _failures++;
            var delay = Backoff(_failures);
            LogFailed(e, _failures, delay);
            return delay;
        }
    }

    /// <summary>Just after the earlier of the two expiries, but at least <see cref="MinInterval"/> away.</summary>
    public static TimeSpan NextPoll(DateTimeOffset now, DateTimeOffset? killsExpire, DateTimeOffset? jumpsExpire)
    {
        DateTimeOffset?[] expiries = [killsExpire, jumpsExpire];
        var next = expiries.OfType<DateTimeOffset>().Where(e => e > now).DefaultIfEmpty(now + DefaultInterval).Min();
        var delay = next + ExpiryMargin - now;
        return delay < MinInterval ? MinInterval : delay;
    }

    /// <summary>1, 2, 4, 8 minutes, then every <see cref="MaxBackoff"/>.</summary>
    public static TimeSpan Backoff(int failures) =>
        TimeSpan.FromTicks(Math.Min(MinInterval.Ticks << Math.Min(failures - 1, 10), MaxBackoff.Ticks));

    [LoggerMessage(Level = LogLevel.Information, Message = "Stored ESI system {Kind} for the hour ending {PeriodEnd:u}: {Systems} systems")]
    private partial void LogStored(string kind, DateTimeOffset periodEnd, int systems);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not collect ESI system kills and jumps (attempt {Attempt}); retrying in {Delay}")]
    private partial void LogFailed(Exception exception, int attempt, TimeSpan delay);
}

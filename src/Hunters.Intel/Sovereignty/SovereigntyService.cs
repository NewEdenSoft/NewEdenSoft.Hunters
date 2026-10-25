using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;

namespace NewEdenSoft.Hunters.Intel.Sovereignty;

/// <summary>Who holds which system, for telling a local from a passer-by.</summary>
public sealed class SovereigntyMap(IEnumerable<SovereignClaim> claims)
{
    public static readonly SovereigntyMap Empty = new([]);

    private readonly FrozenDictionary<int, SovereignClaim> _claims = claims.ToFrozenDictionary(c => c.SystemId);

    public int Count => _claims.Count;

    /// <summary>
    /// Whether <paramref name="allianceId"/> held the system at <paramref name="time"/>, as far as the
    /// current claim tells: before it was claimed, the holder is not known, so nobody is local.
    /// </summary>
    public bool IsLocal(int systemId, long? allianceId, DateTimeOffset time) =>
        allianceId is { } alliance and not 0
        && _claims.TryGetValue(systemId, out var claim)
        && claim.AllianceId == alliance
        && (claim.ClaimedSince is not { } since || since <= time);
}

/// <summary>The current sovereignty map, once there is one (from the database at startup, or ESI).</summary>
public sealed class SovereigntyStore
{
    private readonly TaskCompletionSource<SovereigntyMap> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile SovereigntyMap? _current;

    /// <summary>The map; empty until the first one is in.</summary>
    public SovereigntyMap Current => _current ?? SovereigntyMap.Empty;

    public Task<SovereigntyMap> WhenReadyAsync(CancellationToken cancellationToken) => _first.Task.WaitAsync(cancellationToken);

    public void Publish(SovereigntyMap map)
    {
        _current = map;
        _first.TrySetResult(map);
    }
}

/// <summary>
/// Keeps <see cref="SystemSovereignty"/> and the <see cref="SovereigntyStore"/> current
/// (docs/map-and-activity.md, "Sovereignty"). At startup the stored map is published at once, so
/// matching doesn't wait for ESI; then ESI's list replaces it whenever ESI has a new one (hourly), and
/// failures back off like the other collectors.
/// </summary>
public sealed partial class SovereigntyService(
    ISovereigntySource source,
    SovereigntyStore store,
    IServiceScopeFactory scopes,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<SovereigntyService> logger) : BackgroundService
{
    /// <summary>When ESI doesn't say when its next list is due.</summary>
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

    /// <summary>A little past ESI's expiry, so the next list is there.</summary>
    private static readonly TimeSpan AfterExpiry = TimeSpan.FromSeconds(30);

    private int _failures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Kills.Enabled)
            return;

        try
        {
            await LoadStoredAsync(stoppingToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            LogLoadFailed(e);
        }

        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(await RefreshAsync(stoppingToken), time, stoppingToken);
    }

    /// <summary>Publishes the stored map, if there is one.</summary>
    public async Task LoadStoredAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var stored = await db.SystemSovereignty.AsNoTracking()
            .Select(s => new SovereignClaim(s.SystemId, s.AllianceId, s.CorporationId, s.ClaimedSince))
            .ToListAsync(cancellationToken);
        if (stored.Count > 0)
            store.Publish(new SovereigntyMap(stored));
    }

    /// <summary>Fetches ESI's list, stores and publishes it. Returns how long to wait before the next.</summary>
    public async Task<TimeSpan> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            var list = await source.GetAsync(cancellationToken);
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
                var now = time.GetUtcNow();
                await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
                {
                    db.ChangeTracker.Clear();
                    await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                    await db.SystemSovereignty.ExecuteDeleteAsync(cancellationToken);
                    db.SystemSovereignty.AddRange(list.Claims.Select(c => new SystemSovereignty
                    {
                        SystemId = c.SystemId,
                        AllianceId = c.AllianceId,
                        CorporationId = c.CorporationId,
                        ClaimedSince = c.ClaimedSince,
                        UpdatedAt = now,
                    }));
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                });
            }

            store.Publish(new SovereigntyMap(list.Claims));
            _failures = 0;
            LogUpdated(list.Claims.Count);

            var next = list.Expires is { } expires ? expires + AfterExpiry - time.GetUtcNow() : DefaultInterval;
            return next > TimeSpan.Zero ? next : AfterExpiry;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _failures++;
            var delay = SystemActivityService.Backoff(_failures);
            LogFailed(e, _failures, delay);
            return delay;
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sovereignty updated: {Systems} systems held by alliances")]
    private partial void LogUpdated(int systems);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the stored sovereignty map; waiting for ESI's")]
    private partial void LogLoadFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read sovereignty from ESI (attempt {Attempt}); retrying in {Delay}")]
    private partial void LogFailed(Exception exception, int attempt, TimeSpan delay);
}

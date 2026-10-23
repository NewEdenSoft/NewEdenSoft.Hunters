using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Sde;
using Npgsql;
using NpgsqlTypes;

namespace NewEdenSoft.Hunters.Intel.Hunting;

/// <summary>
/// Rebuilds <see cref="HuntingSystemStats"/> and <see cref="HuntingRegionStats"/> from the sightings
/// (docs/map-and-activity.md, "Hunting aggregates"), with the <c>hunting</c> version bump, in one
/// transaction. Sightings are few (about one kill in ten has one), so a full recompute in SQL is cheap
/// and always right, also after a backfill or when a battle marks earlier sightings as fleet.
/// </summary>
public sealed partial class HuntingAggregator(
    IServiceScopeFactory scopes,
    SdeCatalogStore catalogs,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<HuntingAggregator> logger)
{
    /// <summary>Only one recompute at a time, across processes.</summary>
    private const long AdvisoryLock = 0x48_49_48_55_4E; // "HIHUN"

    public async Task UpdateAsync(CancellationToken cancellationToken)
    {
        var catalog = await catalogs.WhenReadyAsync(cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var now = time.GetUtcNow();

        var (systems, regions) = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [AdvisoryLock], cancellationToken);

            await db.HuntingSystemStats.ExecuteDeleteAsync(cancellationToken);
            await db.HuntingRegionStats.ExecuteDeleteAsync(cancellationToken);
            var systemRows = await RecomputeAsync(db, "HuntingSystemStats", "SystemId", regionOf: null, now, cancellationToken);
            var regionRows = await RecomputeAsync(db, "HuntingRegionStats", "RegionId", catalog, now, cancellationToken);
            await BumpVersionAsync(db, now, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return (systemRows, regionRows);
        });

        LogUpdated(systems, regions);
    }

    /// <summary>
    /// One row per profile and system (or region): the windows, the decayed score and the hours of the
    /// day, from the sightings that count (not fleet fights).
    /// </summary>
    private Task<int> RecomputeAsync(IntelDbContext db, string table, string key, SdeCatalog? regionOf, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var settings = options.Value.Hunting;
        var parameters = new List<NpgsqlParameter>
        {
            new("now", now),
            new("halfLifeSeconds", settings.ScoreHalfLife.TotalSeconds),
        };

        // Sightings with the id they are grouped by: the system, or its region (systems the SDE doesn't know are left out).
        string source;
        if (regionOf is null)
        {
            source = """SELECT "Profile", "SystemId" AS id, "Time", "Weight", "Indicator", "PilotId" FROM intel."Sightings" WHERE NOT "IsFleet" """;
        }
        else
        {
            var systems = regionOf.Systems.Values.ToList();
            parameters.Add(new NpgsqlParameter("systems", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = systems.Select(s => s.Id).ToArray() });
            parameters.Add(new NpgsqlParameter("regions", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = systems.Select(s => s.RegionId).ToArray() });
            source = """
                SELECT s."Profile", m.region AS id, s."Time", s."Weight", s."Indicator", s."PilotId"
                FROM intel."Sightings" s JOIN unnest(@systems, @regions) AS m(system, region) ON m.system = s."SystemId"
                WHERE NOT s."IsFleet"
                """;
        }

        // Table and column names come from this class, never from input.
        var sql = $"""
            WITH src AS ({source}),
            hourly AS (
                SELECT "Profile", id, extract(hour FROM "Time" AT TIME ZONE 'UTC')::int AS hour, sum("Weight") AS weight
                FROM src GROUP BY 1, 2, 3),
            hours AS (
                SELECT k."Profile", k.id, array_agg(coalesce(h.weight, 0) ORDER BY g) AS hours
                FROM (SELECT DISTINCT "Profile", id FROM hourly) k
                CROSS JOIN generate_series(0, 23) g
                LEFT JOIN hourly h ON h."Profile" = k."Profile" AND h.id = k.id AND h.hour = g
                GROUP BY 1, 2),
            totals AS (
                SELECT "Profile", id,
                    coalesce(sum("Weight") FILTER (WHERE "Time" >= @now - interval '7 days'), 0) AS week,
                    coalesce(sum("Weight") FILTER (WHERE "Time" >= @now - interval '30 days'), 0) AS month,
                    coalesce(sum("Weight") FILTER (WHERE "Time" >= @now - interval '30 days' AND "Indicator"), 0) AS month_indicator,
                    sum("Weight" * power(0.5, greatest(extract(epoch FROM @now - "Time"), 0) / @halfLifeSeconds)) AS score,
                    max("Time") AS last_seen,
                    count(DISTINCT "PilotId") FILTER (WHERE "PilotId" <> 0 AND "Time" >= @now - interval '30 days') AS pilots
                FROM src GROUP BY 1, 2)
            INSERT INTO intel."{table}" ("Profile", "{key}", "Week", "Month", "MonthIndicator", "Score", "LastSeen", "Pilots", "Hours")
            SELECT t."Profile", t.id, t.week, t.month, t.month_indicator, t.score, t.last_seen, t.pilots, h.hours
            FROM totals t JOIN hours h USING ("Profile", id)
            """;
        return db.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }

    private static async Task BumpVersionAsync(IntelDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var version = await db.Versions.FindAsync([IntelVersion.Hunting], cancellationToken);
        if (version is null)
            db.Versions.Add(version = new IntelVersion { Dataset = IntelVersion.Hunting });
        version.Version++;
        version.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hunting aggregates recomputed: {Systems} profile-system rows, {Regions} profile-region rows")]
    private partial void LogUpdated(int systems, int regions);
}

/// <summary>
/// Runs <see cref="HuntingAggregator.UpdateAsync"/> once the profiles are ready, then whenever new
/// sightings arrive (<see cref="Signal"/>), at most every <see cref="HuntingOptions.UpdateInterval"/>, and at
/// least hourly so the 7- and 30-day windows and the decay move on.
/// </summary>
public sealed partial class HuntingAggregatorService(
    HuntingAggregator aggregator,
    HuntingProfileStore profiles,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<HuntingAggregatorService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _signal = new(0, 1);
    private int _failures;

    /// <summary>Tells the service sightings changed. Signals before the next run collapse into it.</summary>
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
                // Another signal got there first.
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Kills.Enabled)
            return;
        await profiles.WhenReadyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var failed = !await RunAsync(stoppingToken);

            // Pace the recomputes, then wait for new sightings or the hourly tick.
            var pause = failed ? SystemActivityService.Backoff(_failures) : options.Value.Hunting.UpdateInterval;
            await Task.Delay(pause, time, stoppingToken);
            if (failed)
                continue;

            using var timeout = new CancellationTokenSource(Interval - pause, time);
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeout.Token);
            try
            {
                await _signal.WaitAsync(wait.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // The hour passed without new sightings: refresh the windows and the decay anyway.
            }
        }
    }

    /// <summary>One recompute. Returns whether it succeeded.</summary>
    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await aggregator.UpdateAsync(cancellationToken);
            _failures = 0;
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _failures++;
            LogFailed(e, _failures);
            return false;
        }
    }

    public override void Dispose()
    {
        _signal.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not recompute the hunting aggregates (attempt {Attempt})")]
    private partial void LogFailed(Exception exception, int attempt);
}

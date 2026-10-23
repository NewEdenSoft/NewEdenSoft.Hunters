using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Sde;
using Npgsql;
using NpgsqlTypes;

namespace NewEdenSoft.Hunters.Intel.Activity;

/// <summary>
/// Keeps the derived activity tables in step with the raw ESI hours (docs/decisions.md, D22): adds new
/// hours to the hour-of-week sums of every system and region, takes hours that pass the retention out
/// again before deleting them, and rebuilds the current tables (latest hour, the one before, the last
/// 24 hours, usual for the hour, peak now). Everything happens in one transaction with a version bump,
/// so readers never see a half-applied hour.
/// </summary>
public sealed partial class ActivityAggregator(
    IServiceScopeFactory scopes,
    SdeCatalogStore catalogs,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<ActivityAggregator> logger)
{
    /// <summary>The previous hour must end within this much of an hour before the latest, or there is no delta.</summary>
    private static readonly TimeSpan GapTolerance = TimeSpan.FromMinutes(15);

    /// <summary>Only one aggregator at a time, across processes (a rebuild while the service runs).</summary>
    private const long AdvisoryLock = 0x48_49_41_43_54; // "HIACT"

    /// <summary>Raw hours loaded at once (each holds a few thousand systems).</summary>
    private const int BatchSize = 24;

    /// <summary>Applies new hours, expires old ones and refreshes the current tables. Returns whether anything changed.</summary>
    public Task<bool> UpdateAsync(CancellationToken cancellationToken) =>
        InTransactionAsync((db, catalog) => UpdateCoreAsync(db, catalog, cancellationToken), cancellationToken);

    /// <summary>Forgets every derived table and applies all raw hours again, for when the rules change.</summary>
    public Task RebuildAsync(CancellationToken cancellationToken) =>
        InTransactionAsync(async (db, catalog) =>
        {
            await db.SystemActivityWeek.ExecuteDeleteAsync(cancellationToken);
            await db.RegionActivityWeek.ExecuteDeleteAsync(cancellationToken);
            await db.ActivityWeekSamples.ExecuteDeleteAsync(cancellationToken);
            await db.SystemActivityCurrent.ExecuteDeleteAsync(cancellationToken);
            await db.RegionActivityCurrent.ExecuteDeleteAsync(cancellationToken);
            await db.SystemKills.ExecuteUpdateAsync(s => s.SetProperty(k => k.Applied, false), cancellationToken);
            await db.SystemJumps.ExecuteUpdateAsync(s => s.SetProperty(j => j.Applied, false), cancellationToken);
            return await UpdateCoreAsync(db, catalog, cancellationToken);
        }, cancellationToken);

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction holding the aggregator lock, through the context's
    /// execution strategy: on a transient failure the whole unit runs again from a clean change tracker.
    /// </summary>
    private async Task<bool> InTransactionAsync(Func<IntelDbContext, SdeCatalog, Task<bool>> work, CancellationToken cancellationToken)
    {
        var catalog = await catalogs.WhenReadyAsync(cancellationToken);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock({0})", [AdvisoryLock], cancellationToken);
            var changed = await work(db, catalog);
            await transaction.CommitAsync(cancellationToken);
            return changed;
        });
    }

    private async Task<bool> UpdateCoreAsync(IntelDbContext db, SdeCatalog catalog, CancellationToken cancellationToken)
    {
        var cutoff = time.GetUtcNow() - options.Value.Activity.Retention;

        // Hours past the retention: out of the sums (if they were in), then gone.
        var expired = await InBatchesAsync(db, db.SystemKills.Where(k => k.PeriodEnd < cutoff), async hour =>
        {
            if (hour.Applied)
                await ApplyKillsAsync(db, catalog, hour, -1, cancellationToken);
            db.SystemKills.Remove(hour);
        }, cancellationToken);
        expired |= await InBatchesAsync(db, db.SystemJumps.Where(j => j.PeriodEnd < cutoff), async hour =>
        {
            if (hour.Applied)
                await ApplyJumpsAsync(db, catalog, hour, -1, cancellationToken);
            db.SystemJumps.Remove(hour);
        }, cancellationToken);

        if (expired)
        {
            // Sums that went back to zero: a missing row means zero.
            await db.Database.ExecuteSqlRawAsync(
                """DELETE FROM intel."SystemActivityWeek" WHERE "NpcKills" = 0 AND "ShipKills" = 0 AND "PodKills" = 0 AND "ShipJumps" = 0""", cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                """DELETE FROM intel."RegionActivityWeek" WHERE "NpcKills" = 0 AND "ShipKills" = 0 AND "PodKills" = 0 AND "ShipJumps" = 0""", cancellationToken);
        }

        // New hours: into the sums.
        var applied = await InBatchesAsync(db, db.SystemKills.Where(k => !k.Applied), async hour =>
        {
            await ApplyKillsAsync(db, catalog, hour, 1, cancellationToken);
            hour.Applied = true;
        }, cancellationToken);
        applied |= await InBatchesAsync(db, db.SystemJumps.Where(j => !j.Applied), async hour =>
        {
            await ApplyJumpsAsync(db, catalog, hour, 1, cancellationToken);
            hour.Applied = true;
        }, cancellationToken);

        var changed = expired || applied;
        if (!changed && await db.SystemActivityCurrent.AnyAsync(cancellationToken))
            return false;

        await RefreshCurrentAsync(db, catalog, cancellationToken);
        await BumpVersionAsync(db, cancellationToken);
        return true;
    }

    /// <summary>
    /// Runs <paramref name="each"/> over the raw hours a query finds, a day's worth at a time, saving after
    /// each batch. The query must stop finding an hour once it is handled (applied, or removed).
    /// </summary>
    private static async Task<bool> InBatchesAsync<T>(IntelDbContext db, IQueryable<T> hours, Func<T, Task> each, CancellationToken cancellationToken)
        where T : class
    {
        var any = false;
        var ordered = hours.OrderBy(h => EF.Property<DateTimeOffset>(h, "PeriodEnd"));
        while (await ordered.Take(BatchSize).ToListAsync(cancellationToken) is { Count: > 0 } batch)
        {
            foreach (var hour in batch)
                await each(hour);
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            any = true;
        }

        return any;
    }

    private static async Task ApplyKillsAsync(IntelDbContext db, SdeCatalog catalog, SystemKillsSnapshot hour, int sign, CancellationToken cancellationToken)
    {
        var bucket = HourOfWeek.Covered(hour.PeriodEnd);
        await AddToWeekAsync(db, "SystemActivityWeek", "SystemId", bucket, sign, hour.SystemIds, hour.NpcKills, hour.ShipKills, hour.PodKills, null, cancellationToken);

        var regions = ByRegion(catalog, hour.SystemIds, hour.NpcKills, hour.ShipKills, hour.PodKills);
        await AddToWeekAsync(db, "RegionActivityWeek", "RegionId", bucket, sign,
            regions.Ids, regions.Values[0], regions.Values[1], regions.Values[2], null, cancellationToken);

        await AddSamplesAsync(db, bucket, kills: sign, jumps: 0, cancellationToken);
    }

    private static async Task ApplyJumpsAsync(IntelDbContext db, SdeCatalog catalog, SystemJumpsSnapshot hour, int sign, CancellationToken cancellationToken)
    {
        var bucket = HourOfWeek.Covered(hour.PeriodEnd);
        await AddToWeekAsync(db, "SystemActivityWeek", "SystemId", bucket, sign, hour.SystemIds, null, null, null, hour.ShipJumps, cancellationToken);

        var regions = ByRegion(catalog, hour.SystemIds, hour.ShipJumps);
        await AddToWeekAsync(db, "RegionActivityWeek", "RegionId", bucket, sign, regions.Ids, null, null, null, regions.Values[0], cancellationToken);

        await AddSamplesAsync(db, bucket, kills: 0, jumps: sign, cancellationToken);
    }

    /// <summary>Adds (or with sign −1, takes away) one hour's counts to a week table, in one statement. A null metric adds nothing.</summary>
    private static Task AddToWeekAsync(
        IntelDbContext db, string table, string key, short bucket, int sign,
        int[] ids, int[]? npc, int[]? ship, int[]? pod, int[]? jumps, CancellationToken cancellationToken)
    {
        if (ids.Length == 0)
            return Task.CompletedTask;

        var zeros = new int[ids.Length];
        // Table and column names come from this class, never from input.
        var sql = $"""
            INSERT INTO intel."{table}" AS t ("{key}", "HourOfWeek", "NpcKills", "ShipKills", "PodKills", "ShipJumps")
            SELECT u.id, @bucket, u.npc::bigint * @sign, u.ship::bigint * @sign, u.pod::bigint * @sign, u.jumps::bigint * @sign
            FROM unnest(@ids, @npc, @ship, @pod, @jumps) AS u(id, npc, ship, pod, jumps)
            ON CONFLICT ("{key}", "HourOfWeek") DO UPDATE SET
                "NpcKills" = t."NpcKills" + excluded."NpcKills",
                "ShipKills" = t."ShipKills" + excluded."ShipKills",
                "PodKills" = t."PodKills" + excluded."PodKills",
                "ShipJumps" = t."ShipJumps" + excluded."ShipJumps"
            """;
        return db.Database.ExecuteSqlRawAsync(sql,
        [
            new NpgsqlParameter("bucket", bucket),
            new NpgsqlParameter("sign", sign),
            IntArray("ids", ids),
            IntArray("npc", npc ?? zeros),
            IntArray("ship", ship ?? zeros),
            IntArray("pod", pod ?? zeros),
            IntArray("jumps", jumps ?? zeros),
        ], cancellationToken);
    }

    private static Task AddSamplesAsync(IntelDbContext db, short bucket, int kills, int jumps, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlRawAsync("""
            INSERT INTO intel."ActivityWeekSamples" AS t ("HourOfWeek", "KillHours", "JumpHours") VALUES (@bucket, @kills, @jumps)
            ON CONFLICT ("HourOfWeek") DO UPDATE SET "KillHours" = t."KillHours" + excluded."KillHours", "JumpHours" = t."JumpHours" + excluded."JumpHours"
            """,
            [new NpgsqlParameter("bucket", bucket), new NpgsqlParameter("kills", kills), new NpgsqlParameter("jumps", jumps)],
            cancellationToken);

    private static NpgsqlParameter IntArray(string name, int[] values) =>
        new(name, NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = values };

    /// <summary>An hour's counts added up per region; systems the SDE does not know are left out.</summary>
    private static (int[] Ids, int[][] Values) ByRegion(SdeCatalog catalog, int[] systemIds, params int[][] metrics)
    {
        var sums = new Dictionary<int, int[]>();
        for (var i = 0; i < systemIds.Length; i++)
        {
            if (!catalog.Systems.TryGetValue(systemIds[i], out var system))
                continue;
            if (!sums.TryGetValue(system.RegionId, out var row))
                sums[system.RegionId] = row = new int[metrics.Length];
            for (var m = 0; m < metrics.Length; m++)
                row[m] += metrics[m][i];
        }

        var ids = sums.Keys.ToArray();
        return (ids, [.. Enumerable.Range(0, metrics.Length).Select(m => ids.Select(id => sums[id][m]).ToArray())]);
    }

    private async Task RefreshCurrentAsync(IntelDbContext db, SdeCatalog catalog, CancellationToken cancellationToken)
    {
        var killHours = await LatestDayAsync(db.SystemKills, cancellationToken);
        var jumpHours = await LatestDayAsync(db.SystemJumps, cancellationToken);
        if (killHours.Count == 0 && jumpHours.Count == 0)
        {
            await db.SystemActivityCurrent.ExecuteDeleteAsync(cancellationToken);
            await db.RegionActivityCurrent.ExecuteDeleteAsync(cancellationToken);
            return;
        }

        var samples = await db.ActivityWeekSamples.ToDictionaryAsync(s => s.HourOfWeek, cancellationToken);
        var killsEnd = killHours.FirstOrDefault()?.PeriodEnd ?? DateTimeOffset.MinValue;
        var jumpsEnd = jumpHours.FirstOrDefault()?.PeriodEnd ?? DateTimeOffset.MinValue;

        // Latest, previous and 24-hour values per system, from the raw hours.
        var systemNow = new Dictionary<int, Counts>();
        AddHours(systemNow, killHours, k => k.SystemIds, [k => k.NpcKills, k => k.ShipKills, k => k.PodKills], 0);
        AddHours(systemNow, jumpHours, j => j.SystemIds, [j => j.ShipJumps], 3);

        var regionNow = new Dictionary<int, Counts>();
        foreach (var (systemId, counts) in systemNow)
        {
            if (!catalog.Systems.TryGetValue(systemId, out var system))
                continue;
            if (!regionNow.TryGetValue(system.RegionId, out var region))
                regionNow[system.RegionId] = region = new Counts();
            region.Add(counts);
        }

        var killPrevious = HasPrevious(killHours);
        var jumpPrevious = HasPrevious(jumpHours);

        // Usual for the hour and peaks per metric, from the week sums.
        var systemWeek = await WeekStatsAsync(db, "SystemActivityWeek", "SystemId", killsEnd, jumpsEnd, samples, cancellationToken);
        var regionWeek = await WeekStatsAsync(db, "RegionActivityWeek", "RegionId", killsEnd, jumpsEnd, samples, cancellationToken);

        List<(int Id, ActivityMetric[] Metrics)> Rows(IEnumerable<int> ids, Dictionary<int, Counts> now, Dictionary<int, WeekStats> week) =>
        [
            .. ids.Select(id =>
            {
                ActivityMetric[] metrics = [new(), new(), new(), new()];
                Fill(metrics[0], metrics[1], metrics[2], metrics[3], now.GetValueOrDefault(id), week.GetValueOrDefault(id), killPrevious, jumpPrevious);
                return (id, metrics);
            }),
        ];

        // Written as columns in one statement per table: tracking ~5,000 rows of 33 columns in EF took
        // well over 60 MB for a moment, enough to fail under a container memory limit.
        var header = (killsEnd, jumpsEnd, (short)killHours.Count, (short)jumpHours.Count);
        await db.SystemActivityCurrent.ExecuteDeleteAsync(cancellationToken);
        await db.RegionActivityCurrent.ExecuteDeleteAsync(cancellationToken);
        await WriteCurrentAsync(db, "SystemActivityCurrent", "SystemId", header, Rows(systemNow.Keys.Union(systemWeek.Keys), systemNow, systemWeek), cancellationToken);
        await WriteCurrentAsync(db, "RegionActivityCurrent", "RegionId", header, Rows(regionNow.Keys.Union(regionWeek.Keys), regionNow, regionWeek), cancellationToken);
        LogRefreshed(systemNow.Count, regionNow.Count, killsEnd);
    }

    /// <summary>Inserts current rows (the table was just emptied) from column arrays: one statement, a few arrays.</summary>
    private static Task WriteCurrentAsync(
        IntelDbContext db, string table, string key, (DateTimeOffset KillsEnd, DateTimeOffset JumpsEnd, short KillsDayHours, short JumpsDayHours) header,
        List<(int Id, ActivityMetric[] Metrics)> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return Task.CompletedTask;

        string[] fields = ["LastHour", "PreviousHour", "Day", "Typical", "PeakRatio", "HoursToPeak", "PeakHourOfWeek"];
        var parameters = new List<NpgsqlParameter>
        {
            new("killsEnd", header.KillsEnd),
            new("jumpsEnd", header.JumpsEnd),
            new("killsDay", header.KillsDayHours),
            new("jumpsDay", header.JumpsDayHours),
            IntArray("ids", [.. rows.Select(r => r.Id)]),
        };
        var columns = new List<string>();
        var names = new List<string>();
        for (var m = 0; m < ActivityMetrics.All.Length; m++)
        {
            var values = rows.Select(r => r.Metrics[m]).ToList();
            parameters.Add(new NpgsqlParameter($"m{m}last", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = values.Select(v => v.LastHour).ToArray() });
            parameters.Add(new NpgsqlParameter($"m{m}prev", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = values.Select(v => v.PreviousHour).ToArray() });
            parameters.Add(new NpgsqlParameter($"m{m}day", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = values.Select(v => v.Day).ToArray() });
            parameters.Add(new NpgsqlParameter($"m{m}typical", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = values.Select(v => v.Typical).ToArray() });
            parameters.Add(new NpgsqlParameter($"m{m}ratio", NpgsqlDbType.Array | NpgsqlDbType.Double) { Value = values.Select(v => v.PeakRatio).ToArray() });
            parameters.Add(new NpgsqlParameter($"m{m}topeak", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = values.Select(v => v.HoursToPeak).ToArray() });
            parameters.Add(new NpgsqlParameter($"m{m}peak", NpgsqlDbType.Array | NpgsqlDbType.Smallint) { Value = values.Select(v => v.PeakHourOfWeek).ToArray() });
            columns.AddRange(fields.Select(f => $"\"{ActivityMetrics.All[m]}_{f}\""));
            names.AddRange(new[] { "last", "prev", "day", "typical", "ratio", "topeak", "peak" }.Select(n => $"@m{m}{n}"));
        }

        // Table and column names come from this class, never from input.
        var sql = $"""
            INSERT INTO intel."{table}" ("{key}", {string.Join(", ", columns)}, "KillsPeriodEnd", "JumpsPeriodEnd", "KillsDayHours", "JumpsDayHours")
            SELECT u.*, @killsEnd, @jumpsEnd, @killsDay, @jumpsDay
            FROM unnest(@ids, {string.Join(", ", names)}) AS u
            """;
        return db.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
    }

    /// <summary>The raw hours ending in the 24 hours up to the latest one, newest first.</summary>
    private static async Task<List<T>> LatestDayAsync<T>(IQueryable<T> hours, CancellationToken cancellationToken) where T : class
    {
        var all = hours.AsNoTracking().OrderByDescending(h => EF.Property<DateTimeOffset>(h, "PeriodEnd"));
        var latest = await all.Select(h => (DateTimeOffset?)EF.Property<DateTimeOffset>(h, "PeriodEnd")).FirstOrDefaultAsync(cancellationToken);
        if (latest is not { } end)
            return [];
        var from = end - TimeSpan.FromHours(24) + GapTolerance;
        return await all.Where(h => EF.Property<DateTimeOffset>(h, "PeriodEnd") > from).ToListAsync(cancellationToken);
    }

    /// <summary>Whether the second-newest hour ends about an hour before the newest, so a delta means something.</summary>
    private static bool HasPrevious<T>(List<T> newestFirst) where T : class
    {
        if (newestFirst.Count < 2)
            return false;
        var gap = PeriodEnd(newestFirst[0]) - PeriodEnd(newestFirst[1]);
        return (gap - TimeSpan.FromHours(1)).Duration() <= GapTolerance;
    }

    private static DateTimeOffset PeriodEnd(object hour) => hour switch
    {
        SystemKillsSnapshot k => k.PeriodEnd,
        SystemJumpsSnapshot j => j.PeriodEnd,
        _ => throw new ArgumentOutOfRangeException(nameof(hour)),
    };

    private static void AddHours<T>(Dictionary<int, Counts> into, List<T> newestFirst, Func<T, int[]> ids, Func<T, int[]>[] metrics, int firstMetric)
    {
        for (var h = 0; h < newestFirst.Count; h++)
        {
            var hour = newestFirst[h];
            var systemIds = ids(hour);
            var values = metrics.Select(m => m(hour)).ToArray();
            for (var i = 0; i < systemIds.Length; i++)
            {
                if (!into.TryGetValue(systemIds[i], out var counts))
                    into[systemIds[i]] = counts = new Counts();
                for (var m = 0; m < values.Length; m++)
                {
                    var metric = firstMetric + m;
                    counts.Day[metric] += values[m][i];
                    if (h == 0)
                        counts.Last[metric] = values[m][i];
                    else if (h == 1)
                        counts.Previous[metric] = values[m][i];
                }
            }
        }
    }

    private void Fill(ActivityMetric npc, ActivityMetric ship, ActivityMetric pod, ActivityMetric jumps, Counts? now, WeekStats? week, bool killPrevious, bool jumpPrevious)
    {
        ActivityMetric[] metrics = [npc, ship, pod, jumps];
        for (var m = 0; m < metrics.Length; m++)
        {
            var hasPrevious = m < 3 ? killPrevious : jumpPrevious;
            metrics[m].LastHour = now?.Last[m] ?? 0;
            metrics[m].PreviousHour = hasPrevious ? now?.Previous[m] ?? 0 : null;
            metrics[m].Day = now?.Day[m] ?? 0;
            if (week?.Metrics[m] is { } stats)
            {
                metrics[m].Typical = stats.Typical;
                metrics[m].PeakRatio = stats.PeakRatio;
                metrics[m].HoursToPeak = stats.HoursToPeak;
                metrics[m].PeakHourOfWeek = stats.PeakHour;
            }
        }
    }

    /// <summary>Usual-for-the-hour and peak values per id and metric, from one pass over a week table per metric.</summary>
    private async Task<Dictionary<int, WeekStats>> WeekStatsAsync(
        IntelDbContext db, string table, string key, DateTimeOffset killsEnd, DateTimeOffset jumpsEnd,
        Dictionary<short, ActivityWeekSamples> samples, CancellationToken cancellationToken)
    {
        var settings = options.Value.Activity;
        var result = new Dictionary<int, WeekStats>();
        string[] columns = [ActivityMetrics.NpcKills, ActivityMetrics.ShipKills, ActivityMetrics.PodKills, ActivityMetrics.ShipJumps];

        for (var m = 0; m < columns.Length; m++)
        {
            var jumpsMetric = m == 3;
            var end = jumpsMetric ? jumpsEnd : killsEnd;
            if (end == DateTimeOffset.MinValue)
                continue;

            var samplesColumn = jumpsMetric ? "JumpHours" : "KillHours";
            var last = HourOfWeek.Covered(end);
            var now = HourOfWeek.Following(end);
            int SamplesAt(short bucket) => samples.TryGetValue(bucket, out var s) ? jumpsMetric ? s.JumpHours : s.KillHours : 0;
            var peaksAllowed = Enumerable.Range(0, HourOfWeek.Count).All(b => SamplesAt((short)b) >= settings.PeakMinWeeks);

            // Table and column names come from this class, never from input.
            var sql = $"""
                WITH a AS (
                    SELECT w."{key}" AS id, w."HourOfWeek" AS h, w."{columns[m]}"::float8 / s."{samplesColumn}" AS v
                    FROM intel."{table}" w JOIN intel."ActivityWeekSamples" s ON s."HourOfWeek" = w."HourOfWeek"
                    WHERE s."{samplesColumn}" > 0 AND w."{columns[m]}" <> 0),
                m AS (SELECT id, max(v) AS mx FROM a GROUP BY id)
                SELECT a.id AS "Id", m.mx AS "Max",
                    max(a.v) FILTER (WHERE a.h = @last) AS "AtLast",
                    max(a.v) FILTER (WHERE a.h = @now) AS "AtNow",
                    min(((a.h - @now) % 168 + 168) % 168) FILTER (WHERE a.v >= @threshold * m.mx AND ((a.h - @now) % 168 + 168) % 168 < 24) AS "HoursToPeak",
                    (array_agg(a.h ORDER BY a.v DESC, a.h))[1] AS "PeakHour"
                FROM a JOIN m USING (id)
                GROUP BY a.id, m.mx
                """;
            var rows = await db.Database.SqlQueryRaw<WeekRow>(sql,
                [new NpgsqlParameter("last", last), new NpgsqlParameter("now", now), new NpgsqlParameter("threshold", settings.PeakThreshold)])
                .ToListAsync(cancellationToken);

            var haveLastSamples = SamplesAt(last) > 0;
            foreach (var row in rows)
            {
                if (!result.TryGetValue(row.Id, out var stats))
                    result[row.Id] = stats = new WeekStats();

                var peak = peaksAllowed && row.Max >= settings.PeakMinAverage;
                stats.Metrics[m] = new MetricStats(
                    haveLastSamples ? row.AtLast ?? 0 : null,
                    peak ? (row.AtNow ?? 0) / row.Max : null,
                    peak ? (short?)row.HoursToPeak : null,
                    peak ? row.PeakHour : null);
            }
        }

        return result;
    }

    private async Task BumpVersionAsync(IntelDbContext db, CancellationToken cancellationToken)
    {
        var version = await db.Versions.FindAsync([IntelVersion.Activity], cancellationToken);
        if (version is null)
            db.Versions.Add(version = new IntelVersion { Dataset = IntelVersion.Activity });
        version.Version++;
        version.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Latest, previous and 24-hour counts for the four metrics (NPC, ship, pod, jumps).</summary>
    private sealed class Counts
    {
        public int[] Last { get; } = new int[4];

        public int[] Previous { get; } = new int[4];

        public int[] Day { get; } = new int[4];

        public void Add(Counts other)
        {
            for (var m = 0; m < 4; m++)
            {
                Last[m] += other.Last[m];
                Previous[m] += other.Previous[m];
                Day[m] += other.Day[m];
            }
        }
    }

    private sealed record MetricStats(double? Typical, double? PeakRatio, short? HoursToPeak, short? PeakHour);

    private sealed class WeekStats
    {
        public MetricStats?[] Metrics { get; } = new MetricStats?[4];
    }

    /// <summary>One row of the week query.</summary>
    private sealed class WeekRow
    {
        public int Id { get; set; }

        public double Max { get; set; }

        public double? AtLast { get; set; }

        public double? AtNow { get; set; }

        public int? HoursToPeak { get; set; }

        public short? PeakHour { get; set; }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Activity refreshed for the hour ending {PeriodEnd:u}: {Systems} systems, {Regions} regions")]
    private partial void LogRefreshed(int systems, int regions, DateTimeOffset periodEnd);
}

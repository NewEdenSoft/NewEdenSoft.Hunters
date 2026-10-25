using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Hunting;
using NewEdenSoft.Hunters.Intel.Sovereignty;

namespace NewEdenSoft.Hunters.Intel.Kills;

/// <summary>
/// Finds the stored kills whose fitted counts matter but aren't known: kills stored before the counts were
/// kept, with some of a <see cref="ResolvedRule.NeedsCounts"/> rule's types fitted, but fewer distinct ones
/// than the rule needs (five distinct smart bombs prove a smartbomber without the counts; one could be one
/// or five).
/// </summary>
public static class KillFittings
{
    /// <summary>A stored kill to fetch again.</summary>
    public sealed class Candidate
    {
        public long KillmailId { get; init; }

        public string Hash { get; init; } = "";
    }

    /// <summary>The rules whose matching depends on the counts.</summary>
    public static List<ResolvedRule> RulesNeedingCounts(IEnumerable<ResolvedProfile> profiles) =>
        [.. profiles.SelectMany(p => p.Rules).Where(r => r.NeedsCounts && r.Types.Count > 0)];

    /// <summary>Up to <paramref name="limit"/> kills to fetch, newest first: recent kills weigh most in the scores.</summary>
    public static async Task<List<Candidate>> NextAsync(IntelDbContext db, IReadOnlyList<ResolvedRule> rules, int limit, CancellationToken cancellationToken)
    {
        var found = new List<(Candidate Kill, DateTimeOffset Time)>();
        foreach (var rule in rules)
        {
            var types = rule.Types.ToArray();
            var rows = await db.Database.SqlQuery<CandidateRow>($"""
                SELECT k."KillmailId", k."Hash", k."Time" FROM intel."Kills" k
                WHERE cardinality(k."VictimFittedCounts") = 0 AND k."VictimFittedTypeIds" && {types}
                  AND (SELECT count(*) FROM unnest(k."VictimFittedTypeIds") t WHERE t = ANY({types})) < {rule.MinFitted}
                ORDER BY k."Time" DESC
                LIMIT {limit}
                """).ToListAsync(cancellationToken);
            found.AddRange(rows.Select(r => (new Candidate { KillmailId = r.KillmailId, Hash = r.Hash }, r.Time)));
        }

        return [.. found.DistinctBy(f => f.Kill.KillmailId).OrderByDescending(f => f.Time).Take(limit).Select(f => f.Kill)];
    }

    /// <summary>How many kills are left to fetch (a kill several rules want counts once per rule).</summary>
    public static async Task<int> CountAsync(IntelDbContext db, IReadOnlyList<ResolvedRule> rules, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var rule in rules)
        {
            var types = rule.Types.ToArray();
            count += await db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM intel."Kills" k
                WHERE cardinality(k."VictimFittedCounts") = 0 AND k."VictimFittedTypeIds" && {types}
                  AND (SELECT count(*) FROM unnest(k."VictimFittedTypeIds") t WHERE t = ANY({types})) < {rule.MinFitted}
                """).SingleAsync(cancellationToken);
        }

        return count;
    }

    private sealed class CandidateRow
    {
        public long KillmailId { get; init; }

        public string Hash { get; init; } = "";

        public DateTimeOffset Time { get; init; }
    }
}

/// <summary>
/// Fetches stored kills again from ESI for their fitted counts (<see cref="KillFittings"/>), slowly, at
/// <see cref="KillRefetchOptions.PerSecond"/>, so a backlog of thousands drains over hours without
/// troubling ESI. Each kill fetched is matched again against the profiles that need the counts, so they
/// fill back up as it goes. The work list is just "counts not known", so a restart resumes it and, once
/// it is empty, the service only looks again every <see cref="KillRefetchOptions.IdleInterval"/>. One
/// worker at a time, across processes, by a database lock.
/// </summary>
public sealed partial class KillRefetchService(
    IKillmailSource killmails,
    HuntingProfileStore profiles,
    SovereigntyStore sovereignty,
    HuntingAggregatorService aggregates,
    IServiceScopeFactory scopes,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<KillRefetchService> logger) : BackgroundService
{
    /// <summary>Only one refetch at a time, across processes.</summary>
    public const long AdvisoryLock = 0x48_49_52_45_46; // "HIREF"

    private int _failures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Kills.Enabled || !options.Value.Kills.Refetch.Enabled)
            return;
        await profiles.WhenReadyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(await RunAsync(stoppingToken), time, stoppingToken);
    }

    /// <summary>Fetches until nothing is left, ESI asks for a pause, or a failure. Returns how long to wait before the next run.</summary>
    public async Task<TimeSpan> RunAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Kills.Refetch;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();

            // A session lock on this connection: another worker leaves the refetch alone. It is released
            // explicitly, as a pooled connection only resets its session when next used.
            await db.Database.OpenConnectionAsync(cancellationToken);
            var locked = await db.Database.SqlQuery<bool>($"""SELECT pg_try_advisory_lock({AdvisoryLock}) AS "Value" """).SingleAsync(cancellationToken);
            if (!locked)
                return settings.IdleInterval;

            try
            {
                await DrainAsync(db, settings, cancellationToken);
            }
            finally
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({AdvisoryLock})", CancellationToken.None);
            }

            _failures = 0;
            return settings.IdleInterval;
        }
        catch (EsiThrottledException e)
        {
            LogThrottled(e.Message, e.RetryAfter);
            return e.RetryAfter;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _failures++;
            var delay = SystemActivityService.Backoff(_failures);
            LogFailed(e, _failures, delay);
            return delay;
        }
    }

    /// <summary>Fetches batch after batch, newest first, until no kill is left to fetch.</summary>
    private async Task DrainAsync(IntelDbContext db, KillRefetchOptions settings, CancellationToken cancellationToken)
    {
        var hunting = profiles.Current!;
        var rules = KillFittings.RulesNeedingCounts(hunting);
        var left = rules.Count > 0 ? await KillFittings.CountAsync(db, rules, cancellationToken) : 0;
        if (left == 0)
            return;

        LogStarting(left, Eta(left, settings));
        var pace = TimeSpan.FromSeconds(1 / settings.PerSecond);
        while (true)
        {
            var batch = await KillFittings.NextAsync(db, rules, settings.BatchSize, cancellationToken);
            if (batch.Count == 0)
                break;

            foreach (var candidate in batch)
            {
                await RefetchAsync(db, candidate, hunting, cancellationToken);
                await Task.Delay(pace, time, cancellationToken);
            }

            left = Math.Max(0, left - batch.Count);
            _failures = 0;
            LogProgress(left, Eta(left, settings));
        }

        LogDone();
    }

    /// <summary>
    /// Fetches one kill and stores its counts (zeros when ESI no longer has it, so it isn't asked again),
    /// then records the sightings the counts reveal.
    /// </summary>
    private async Task RefetchAsync(IntelDbContext db, KillFittings.Candidate candidate, IReadOnlyList<ResolvedProfile> hunting, CancellationToken cancellationToken)
    {
        var fittings = await killmails.GetFittingsAsync(candidate.KillmailId, candidate.Hash, cancellationToken);
        var kill = await db.Kills.SingleOrDefaultAsync(k => k.KillmailId == candidate.KillmailId, cancellationToken);
        if (kill is null)
            return; // Pruned meanwhile.

        if (fittings is null)
        {
            LogGone(candidate.KillmailId);
            kill.VictimFittedCounts = new int[kill.VictimFittedTypeIds.Length];
        }
        else
        {
            kill.VictimFittedTypeIds = fittings.Types;
            kill.VictimFittedCounts = fittings.Counts;
        }

        await db.SaveChangesAsync(cancellationToken);
        db.ChangeTracker.Clear();
        if (fittings is null)
            return;

        var settings = options.Value.Hunting;
        var needing = hunting.Where(p => p.Rules.Any(r => r.NeedsCounts)).ToList();
        if (await SightingStore.InsertAsync(db, SightingMatcher.Match(kill, needing, settings.FleetAttackers, sovereignty.Current), cancellationToken) == 0)
            return;

        string[] keeping = [.. hunting.Where(p => p.Profile.KeepsFights).Select(p => p.Id)];
        await SightingStore.MarkBattlesAsync(db, settings, kill.SystemId, kill.Time, [.. needing.Select(p => p.Id)], keeping, cancellationToken);
        aggregates.Signal();
    }

    private static TimeSpan Eta(int left, KillRefetchOptions settings) => TimeSpan.FromSeconds(Math.Ceiling(left / settings.PerSecond));

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetching {Left} stored kills again from ESI for their fitted modules; done in about {Eta}")]
    private partial void LogStarting(int left, TimeSpan eta);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Left} stored kills left to fetch again from ESI; done in about {Eta}")]
    private partial void LogProgress(int left, TimeSpan eta);

    [LoggerMessage(Level = LogLevel.Information, Message = "Every stored kill that needed its fitted modules has them")]
    private partial void LogDone();

    [LoggerMessage(Level = LogLevel.Debug, Message = "ESI has no killmail {KillmailId}; its fitted counts stay unknown")]
    private partial void LogGone(long killmailId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pausing the kill refetch: {Reason} Resuming in {Pause}")]
    private partial void LogThrottled(string reason, TimeSpan pause);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not fetch stored kills again from ESI (attempt {Attempt}); retrying in {Delay}")]
    private partial void LogFailed(Exception exception, int attempt, TimeSpan delay);
}

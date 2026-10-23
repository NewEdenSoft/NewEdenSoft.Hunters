using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Hunting;

namespace NewEdenSoft.Hunters.Intel.Kills;

/// <summary>
/// Reads zKillboard's R2Z2 feed around the clock (docs/map-and-activity.md, "Kills") and stores each kill
/// once as a slim <see cref="Kill"/>, with the feed position in the same save so a restart resumes
/// where it stopped, and records the kill's hunting-profile sightings. zKillboard keeps files for about a
/// day: a position older than <see cref="KillsOptions.ResumeWithin"/> skips to the newest. Kills and
/// sightings older than the retention are deleted hourly. Failures are retried with the collector's backoff.
/// </summary>
public sealed partial class KillFeedService(
    IKillFeedSource source,
    IServiceScopeFactory scopes,
    HuntingProfileStore profiles,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<KillFeedService> logger) : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    private long? _next;
    private bool _caughtUp;
    private int _stored;
    private int _failures;
    private DateTimeOffset _nextPrune = DateTimeOffset.MinValue;

    /// <summary>The next sequence number to read; null until the first step.</summary>
    public long? Next => _next;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Kills.Enabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = await StepAsync(stoppingToken);
            await Task.Delay(delay, time, stoppingToken);
        }
    }

    /// <summary>Reads (at most) one file. Returns how long to wait before the next step.</summary>
    public async Task<TimeSpan> StepAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Kills;
        try
        {
            await PruneIfDueAsync(cancellationToken);
            _next ??= await FirstSequenceAsync(cancellationToken);
            var sequence = _next.Value;

            var file = await source.GetAsync(sequence, cancellationToken);
            if (file is null)
            {
                // Above the announced sequence: not written yet. At or below it: gone, or never written.
                if (sequence > await source.GetLatestSequenceAsync(cancellationToken))
                {
                    if (!_caughtUp)
                        LogCaughtUp(sequence, _stored);
                    _caughtUp = true;
                    _failures = 0;
                    return settings.WaitWhenCaughtUp;
                }

                LogMissing(sequence);
                _next = sequence + 1;
                return settings.RequestInterval;
            }

            if (await StoreAsync(sequence, file, await profiles.WhenReadyAsync(cancellationToken), cancellationToken))
                _stored++;
            _next = sequence + 1;
            _failures = 0;
            return settings.RequestInterval;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _failures++;
            var delay = SystemActivityService.Backoff(_failures);
            LogFailed(e, _failures, delay);
            return delay;
        }
    }

    /// <summary>Where to start: after the saved position if it is recent enough, else at the newest file.</summary>
    private async Task<long> FirstSequenceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var saved = await db.FeedPositions.AsNoTracking().FirstOrDefaultAsync(f => f.Feed == FeedPosition.R2Z2, cancellationToken);
        if (saved is not null && time.GetUtcNow() - saved.UpdatedAt <= options.Value.Kills.ResumeWithin)
        {
            LogResuming(saved.Sequence + 1);
            return saved.Sequence + 1;
        }

        var latest = await source.GetLatestSequenceAsync(cancellationToken);
        LogStarting(latest, saved?.UpdatedAt);
        return latest;
    }

    /// <summary>
    /// Records the kill's sightings, then stores the kill (unless it is already stored) and the position in
    /// one save, then marks a battle the kill may have started. Sightings come first and skip what is
    /// already there, so a crash in between only repeats harmless work. Returns whether the kill was new.
    /// </summary>
    private async Task<bool> StoreAsync(long sequence, KillFeedFile file, IReadOnlyList<ResolvedProfile> hunting, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var kill = file.ToKill();
        var settings = options.Value.Hunting;

        await SightingStore.InsertAsync(db, SightingMatcher.Match(kill, hunting, settings.FleetAttackers), cancellationToken);

        // zKillboard can announce a kill again (a repost, a corrected value); the first one stays.
        var isNew = !await db.Kills.AnyAsync(k => k.KillmailId == file.KillmailId, cancellationToken);
        if (isNew)
            db.Kills.Add(kill);

        var position = await db.FeedPositions.FindAsync([FeedPosition.R2Z2], cancellationToken);
        if (position is null)
            db.FeedPositions.Add(position = new FeedPosition { Feed = FeedPosition.R2Z2 });
        position.Sequence = sequence;
        position.UpdatedAt = time.GetUtcNow();

        await db.SaveChangesAsync(cancellationToken);

        // A player kill can tip the system into a battle, marking sightings around it.
        if (isNew && !kill.Npc)
            await SightingStore.MarkBattlesAsync(db, settings, kill.SystemId, kill.Time, null, cancellationToken);
        return isNew;
    }

    private async Task PruneIfDueAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (now < _nextPrune)
            return;

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var cutoff = now - options.Value.Kills.Retention;
        var deleted = await db.Kills.Where(k => k.Time < cutoff).ExecuteDeleteAsync(cancellationToken);
        await db.Sightings.Where(s => s.Time < cutoff).ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
            LogPruned(deleted, cutoff);
        _nextPrune = now + PruneInterval;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Resuming the R2Z2 kill feed at sequence {Sequence}")]
    private partial void LogResuming(long sequence);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting the R2Z2 kill feed at its newest sequence {Sequence} (last read {LastRead})")]
    private partial void LogStarting(long sequence, DateTimeOffset? lastRead);

    [LoggerMessage(Level = LogLevel.Information, Message = "Caught up with the R2Z2 kill feed at sequence {Sequence}; {Stored} kills stored since starting")]
    private partial void LogCaughtUp(long sequence, int stored);

    [LoggerMessage(Level = LogLevel.Debug, Message = "R2Z2 has no file for sequence {Sequence}; skipping it")]
    private partial void LogMissing(long sequence);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} kills from before {Cutoff:u}")]
    private partial void LogPruned(int count, DateTimeOffset cutoff);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read the R2Z2 kill feed (attempt {Attempt}); retrying in {Delay}")]
    private partial void LogFailed(Exception exception, int attempt, TimeSpan delay);
}

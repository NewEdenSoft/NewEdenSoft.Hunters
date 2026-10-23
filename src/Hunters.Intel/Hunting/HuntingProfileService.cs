using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Sde;

namespace NewEdenSoft.Hunters.Intel.Hunting;

/// <summary>The hunting profiles resolved against the current SDE, once they are.</summary>
public sealed class HuntingProfileStore
{
    private readonly TaskCompletionSource<IReadOnlyList<ResolvedProfile>> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile IReadOnlyList<ResolvedProfile>? _current;

    public IReadOnlyList<ResolvedProfile>? Current => _current;

    public Task<IReadOnlyList<ResolvedProfile>> WhenReadyAsync(CancellationToken cancellationToken) => _first.Task.WaitAsync(cancellationToken);

    public void Publish(IReadOnlyList<ResolvedProfile> profiles)
    {
        _current = profiles;
        _first.TrySetResult(profiles);
    }
}

/// <summary>
/// Resolves <see cref="HuntingProfiles.All"/> against the SDE catalogue at startup and on every new
/// catalogue, and keeps the stored sightings in step with the definitions: a profile whose resolved
/// definition changed (or that is new) has its sightings rebuilt from the stored kills, and a profile
/// that was removed loses them.
/// </summary>
public sealed partial class HuntingProfileService(
    SdeCatalogStore catalogs,
    HuntingProfileStore store,
    HuntingAggregatorService aggregates,
    IServiceScopeFactory scopes,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<HuntingProfileService> logger) : BackgroundService
{
    /// <summary>How often to look for a new catalogue.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(10);

    /// <summary>Kills read at once while backfilling.</summary>
    private const int BatchSize = 2000;

    private SdeCatalog? _synced;
    private int _failures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                var catalog = await catalogs.WhenReadyAsync(stoppingToken);
                if (!ReferenceEquals(catalog, _synced))
                    await SyncAsync(catalog, stoppingToken);
                _failures = 0;
                delay = CheckInterval;
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                _failures++;
                delay = SystemActivityService.Backoff(_failures);
                LogFailed(e, _failures, delay);
            }

            await Task.Delay(delay, time, stoppingToken);
        }
    }

    /// <summary>Resolves the profiles, publishes them, and backfills the ones whose definition changed.</summary>
    public async Task SyncAsync(SdeCatalog catalog, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var profiles = HuntingProfiles.All.Select(p => ResolvedProfile.Resolve(p, catalog, problems)).ToList();
        foreach (var problem in problems)
            LogProblem(problem);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var states = await db.HuntingProfileStates.ToDictionaryAsync(s => s.Profile, cancellationToken);

        var removed = states.Keys.Except(profiles.Select(p => p.Id)).ToArray();
        var changed = profiles.Where(p => states.GetValueOrDefault(p.Id)?.Definition != p.Definition).ToList();
        var changedIds = changed.Select(p => p.Id).ToArray();

        // Out with the old sightings first, so live kills recorded from here on and the backfill both
        // use the new definitions (inserts skip what is already there).
        string[] cleared = [.. removed, .. changedIds];
        if (cleared.Length > 0)
            await db.Sightings.Where(s => cleared.Contains(s.Profile)).ExecuteDeleteAsync(cancellationToken);
        if (removed.Length > 0)
            await db.HuntingProfileStates.Where(s => removed.Contains(s.Profile)).ExecuteDeleteAsync(cancellationToken);

        store.Publish(profiles);

        var started = time.GetTimestamp();
        var (kills, sightings) = changed.Count > 0 ? await BackfillAsync(db, changed, cancellationToken) : (0, 0);

        // Every profile's name (readers list the profiles from here), and the definition of the rebuilt ones.
        foreach (var profile in profiles)
        {
            var state = states.GetValueOrDefault(profile.Id);
            if (state is null)
                db.HuntingProfileStates.Add(state = new HuntingProfileState { Profile = profile.Id });
            state.Name = profile.Profile.Name;
            if (changed.Contains(profile))
            {
                state.Definition = profile.Definition;
                state.BackfilledAt = time.GetUtcNow();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (changed.Count > 0)
            LogBackfilled(string.Join(", ", changedIds), kills, sightings, time.GetElapsedTime(started));
        if (cleared.Length > 0)
            aggregates.Signal();

        _synced = catalog;
    }

    /// <summary>Matches every stored kill against <paramref name="profiles"/>, oldest first, then marks the battles.</summary>
    private async Task<(int Kills, int Sightings)> BackfillAsync(IntelDbContext db, List<ResolvedProfile> profiles, CancellationToken cancellationToken)
    {
        var settings = options.Value.Hunting;
        var kills = 0;
        var sightings = 0;
        var after = long.MinValue;
        while (true)
        {
            var batch = await db.Kills.AsNoTracking()
                .Where(k => k.KillmailId > after)
                .OrderBy(k => k.KillmailId)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0)
                break;

            var found = batch.SelectMany(k => SightingMatcher.Match(k, profiles, settings.FleetAttackers)).ToList();
            await SightingStore.InsertAsync(db, found, cancellationToken);
            kills += batch.Count;
            sightings += found.Count;
            after = batch[^1].KillmailId;
        }

        await SightingStore.MarkBattlesAsync(db, settings, null, null, [.. profiles.Select(p => p.Id)],
            [.. profiles.Where(p => p.Profile.KeepsFights).Select(p => p.Id)], cancellationToken);
        return (kills, sightings);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hunting profile {Problem}; that part of the profile is left out")]
    private partial void LogProblem(string problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backfilled hunting profiles {Profiles} from {Kills} kills: {Sightings} sightings found in {Elapsed}")]
    private partial void LogBackfilled(string profiles, int kills, int sightings, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not sync the hunting profiles (attempt {Attempt}); retrying in {Delay}")]
    private partial void LogFailed(Exception exception, int attempt, TimeSpan delay);
}

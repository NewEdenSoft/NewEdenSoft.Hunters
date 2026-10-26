using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Kills;
using NewEdenSoft.Hunters.Intel.Sde;
using NewEdenSoft.Hunters.Intel.Sovereignty;

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
/// that was removed loses them. Before a profile that looks at the attackers' side is rebuilt, stored
/// kills without the attackers' corporations get them (<see cref="KillAffiliations"/>). An admin's
/// <see cref="HuntingRebuildRequest"/> clears the stored definition of the profiles it names, so they
/// are rebuilt the same way; it is marked done once they are, and retried until then.
/// </summary>
public sealed partial class HuntingProfileService(
    SdeCatalogStore catalogs,
    HuntingProfileStore store,
    SovereigntyStore sovereignty,
    HuntingAggregatorService aggregates,
    IAffiliationSource affiliations,
    IServiceScopeFactory scopes,
    IOptions<IntelOptions> options,
    TimeProvider time,
    ILogger<HuntingProfileService> logger) : BackgroundService
{
    /// <summary>How often to look for a new catalogue and for rebuild requests.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    /// <summary>The longest error kept on a rebuild request (its column's length).</summary>
    private const int MaxError = 1000;

    /// <summary>Kills read at once while backfilling.</summary>
    private const int BatchSize = 2000;

    private SdeCatalog? _synced;
    private int _failures;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(await StepAsync(stoppingToken), time, stoppingToken);
    }

    /// <summary>
    /// Syncs with a new catalogue, and rebuilds what admins asked for. Returns how long to wait before the
    /// next step: a failure backs off, and its rebuild requests stay open to be tried again.
    /// </summary>
    public async Task<TimeSpan> StepAsync(CancellationToken cancellationToken)
    {
        long[] requests = [];
        try
        {
            var catalog = await catalogs.WhenReadyAsync(cancellationToken);
            // Sightings are built with who is local, so not before the first sovereignty map.
            await sovereignty.WhenReadyAsync(cancellationToken);
            requests = await TakeRequestsAsync(cancellationToken);
            if (requests.Length > 0 || !ReferenceEquals(catalog, _synced))
                await SyncAsync(catalog, cancellationToken);
            if (requests.Length > 0)
                await CompleteRequestsAsync(requests, null, cancellationToken);
            _failures = 0;
            return CheckInterval;
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _failures++;
            var delay = SystemActivityService.Backoff(_failures);
            LogFailed(e, _failures, delay);
            if (requests.Length > 0)
                await TryNoteErrorAsync(requests, e, cancellationToken);
            return delay;
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
        {
            await db.Sightings.Where(s => cleared.Contains(s.Profile)).ExecuteDeleteAsync(cancellationToken);
            await db.HuntingAppearances.Where(a => cleared.Contains(a.Profile)).ExecuteDeleteAsync(cancellationToken);
        }

        if (removed.Length > 0)
            await db.HuntingProfileStates.Where(s => removed.Contains(s.Profile)).ExecuteDeleteAsync(cancellationToken);

        store.Publish(profiles);

        var started = time.GetTimestamp();
        if (changed.Any(p => p.Rules.Any(r => r.Side != VictimSide.Any)))
        {
            var filled = await KillAffiliations.FillAsync(db, affiliations, cancellationToken);
            if (filled > 0)
                LogAffiliationsFilled(filled, time.GetElapsedTime(started));
        }

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

    /// <summary>
    /// Takes up the open rebuild requests: clears the stored definition of the profiles they name (all, if
    /// one names none), so the next sync rebuilds them. Returns their ids; empty when there are none.
    /// </summary>
    private async Task<long[]> TakeRequestsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var open = await db.HuntingRebuildRequests.Where(r => r.CompletedAt == null).OrderBy(r => r.Id).ToListAsync(cancellationToken);
        if (open.Count == 0)
            return [];

        var now = time.GetUtcNow();
        foreach (var request in open)
            request.StartedAt ??= now;

        var all = open.Any(r => r.Profiles.Length == 0);
        string[] named = [.. open.SelectMany(r => r.Profiles).Distinct()];
        await db.HuntingProfileStates
            .Where(s => all || named.Contains(s.Profile))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Definition, ""), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        LogRebuildRequested(all ? "all" : string.Join(", ", named), open.Count);
        return [.. open.Select(r => r.Id)];
    }

    private async Task CompleteRequestsAsync(long[] ids, string? error, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var now = time.GetUtcNow();
        await db.HuntingRebuildRequests.Where(r => ids.Contains(r.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.CompletedAt, error is null ? now : null).SetProperty(r => r.Error, error), cancellationToken);
    }

    /// <summary>Notes why the rebuild failed on its requests, which stay open and are tried again.</summary>
    private async Task TryNoteErrorAsync(long[] ids, Exception e, CancellationToken cancellationToken)
    {
        try
        {
            var message = e.Message.Length > MaxError ? e.Message[..MaxError] : e.Message;
            await CompleteRequestsAsync(ids, message, cancellationToken);
        }
        catch (Exception noted) when (noted is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The database is likely what failed; the error is logged already.
        }
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

            var found = await SightingStore.RecordAsync(db, batch, profiles, settings.FleetAttackers, sovereignty.Current, cancellationToken);
            kills += batch.Count;
            sightings += found;
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Filled in the attackers' corporations of {Kills} stored kills from ESI in {Elapsed}")]
    private partial void LogAffiliationsFilled(int kills, TimeSpan elapsed);

    [LoggerMessage(Level = LogLevel.Information, Message = "An admin asked to rebuild hunting profiles {Profiles} ({Requests} requests)")]
    private partial void LogRebuildRequested(string profiles, int requests);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not sync the hunting profiles (attempt {Attempt}); retrying in {Delay}")]
    private partial void LogFailed(Exception exception, int attempt, TimeSpan delay);
}

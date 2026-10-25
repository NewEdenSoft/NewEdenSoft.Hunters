using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Server.Configuration;
using NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;
using Npgsql;

namespace NewEdenSoft.Hunters.Server.Features.Hunting;

/// <summary>One version of the intel service's hunting aggregates, held in memory (a few thousand small rows).</summary>
public sealed class HuntingSnapshot
{
    public HuntingSnapshot(long version, DateTimeOffset updatedAt, IReadOnlyList<HuntingProfileInfo> profiles,
        IReadOnlyList<HuntingSystemStats> systems, IReadOnlyList<HuntingRegionStats> regions)
    {
        Version = version;
        UpdatedAt = updatedAt;
        Profiles = profiles;
        var names = profiles.ToDictionary(p => p.Id, p => p.Name);

        Systems = systems.GroupBy(s => s.SystemId).ToDictionary(g => g.Key, g => Place(g.Select(s => Row(s.Profile, s.Score, s.Week, s.Month, s.MonthIndicator, s.Pilots, s.LastSeen, s.Hours, s.MonthLocal, s.LocalPilots))));
        Regions = regions.GroupBy(r => r.RegionId).ToDictionary(g => g.Key, g => Place(g.Select(r => Row(r.Profile, r.Score, r.Week, r.Month, r.MonthIndicator, r.Pilots, r.LastSeen, r.Hours, r.MonthLocal, r.LocalPilots))));

        // Profiles the states don't list any more are left out.
        Overview = PrecompressedJson.Create(new HuntingOverview(version, updatedAt, [
            .. profiles.Select(p =>
            {
                var rows = systems.Where(s => s.Profile == p.Id).OrderByDescending(s => s.Score).ThenBy(s => s.SystemId).ToList();
                return new HuntingProfileSystems(
                    p.Id,
                    [.. rows.Select(s => s.SystemId)],
                    [.. rows.Select(s => Round(s.Score))],
                    [.. rows.Select(s => Round(s.Week))],
                    [.. rows.Select(s => Round(s.Month))],
                    [.. rows.Select(s => s.LastSeen)],
                    [.. rows.Select(s => (IReadOnlyList<double>)[.. s.Hours.Select(Round)])]);
            }),
        ]));

        IReadOnlyList<HuntingPlaceProfile> Place(IEnumerable<HuntingPlaceProfile?> rows) =>
            [.. rows.OfType<HuntingPlaceProfile>().OrderByDescending(r => r.Score).ThenBy(r => r.Profile)];

        HuntingPlaceProfile? Row(
            string profile, double score, double week, double month, double indicator, int pilots, DateTimeOffset lastSeen, double[] hours,
            double monthLocal, int localPilots) =>
            names.TryGetValue(profile, out var name)
                ? new HuntingPlaceProfile(profile, name, Round(score), Round(week), Round(month), Round(indicator), pilots, lastSeen, [.. hours.Select(Round)],
                    Round(monthLocal), localPilots)
                : null;
    }

    public long Version { get; }

    public DateTimeOffset UpdatedAt { get; }

    public IReadOnlyList<HuntingProfileInfo> Profiles { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<HuntingPlaceProfile>> Systems { get; }

    public IReadOnlyDictionary<int, IReadOnlyList<HuntingPlaceProfile>> Regions { get; }

    /// <summary>The overview response, built once per version.</summary>
    public PrecompressedJson Overview { get; }

    public string ETag => $"\"hunting-{Version}\"";

    private static double Round(double value) => Math.Round(value, 3);
}

/// <summary>The current hunting snapshot; null until the intel service has computed the aggregates.</summary>
public sealed class HuntingStore
{
    private volatile HuntingSnapshot? _current;

    public HuntingSnapshot? Current => _current;

    public void Publish(HuntingSnapshot? snapshot) => _current = snapshot;
}

/// <summary>
/// Follows the intel service's hunting aggregates like <c>ActivityService</c> follows activity: checks
/// the <c>hunting</c> version every <see cref="ActivityOptions.PollInterval"/> and loads the stats
/// tables and profile names when it moved. The server only reads the <c>intel</c> schema.
/// </summary>
public sealed partial class HuntingService(
    IServiceScopeFactory scopes,
    HuntingStore store,
    IOptions<HuntersOptions> options,
    TimeProvider time,
    ILogger<HuntingService> logger) : BackgroundService
{
    /// <summary>PostgreSQL's undefined_table: the intel service has not migrated its schema yet.</summary>
    private const string UndefinedTable = "42P01";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Activity.Enabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                LogFailed(e);
            }

            await Task.Delay(options.Value.Activity.PollInterval, time, stoppingToken);
        }
    }

    /// <summary>Loads the aggregates when the version moved. Returns whether the snapshot changed.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();

        long? version;
        try
        {
            version = await db.Versions.AsNoTracking()
                .Where(v => v.Dataset == IntelVersion.Hunting)
                .Select(v => (long?)v.Version)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (PostgresException e) when (e.SqlState == UndefinedTable)
        {
            version = null;
        }

        if (version == store.Current?.Version)
            return false;

        store.Publish(version is null ? null : await LoadAsync(db, cancellationToken));
        return true;
    }

    /// <summary>The version row and the tables in one snapshot (through the execution strategy, which retries the whole read).</summary>
    private Task<HuntingSnapshot> LoadAsync(IntelDbContext db, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
            var version = await db.Versions.AsNoTracking().SingleAsync(v => v.Dataset == IntelVersion.Hunting, cancellationToken);
            var profiles = await db.HuntingProfileStates.AsNoTracking().OrderBy(p => p.Name).Select(p => new HuntingProfileInfo(p.Profile, p.Name)).ToListAsync(cancellationToken);
            var systems = await db.HuntingSystemStats.AsNoTracking().ToListAsync(cancellationToken);
            var regions = await db.HuntingRegionStats.AsNoTracking().ToListAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            LogLoaded(version.Version, profiles.Count, systems.Count);
            return new HuntingSnapshot(version.Version, version.UpdatedAt, profiles, systems, regions);
        });

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded hunting version {Version}: {Profiles} profiles, {Rows} profile-system rows")]
    private partial void LogLoaded(long version, int profiles, int rows);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the intel service's hunting aggregates")]
    private partial void LogFailed(Exception exception);
}

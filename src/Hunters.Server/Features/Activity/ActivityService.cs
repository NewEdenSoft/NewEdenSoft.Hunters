using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Server.Configuration;
using Npgsql;

namespace NewEdenSoft.Hunters.Server.Features.Activity;

/// <summary>
/// Follows the intel service's activity tables (docs/decisions.md, D22): checks the <c>activity</c>
/// version every <see cref="ActivityOptions.PollInterval"/> and, when it changed, loads the current
/// tables into <see cref="ActivityStore"/>. The server only reads the <c>intel</c> schema; until the
/// intel service has created it and stored an hour, there is simply no activity.
/// </summary>
public sealed partial class ActivityService(
    IServiceScopeFactory scopes,
    ActivityStore store,
    IOptions<HuntersOptions> options,
    TimeProvider time,
    ILogger<ActivityService> logger) : BackgroundService
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

    /// <summary>Loads the current tables when the version moved. Returns whether the snapshot changed.</summary>
    public async Task<bool> RefreshAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();

        long? version;
        try
        {
            version = await db.Versions.AsNoTracking()
                .Where(v => v.Dataset == IntelVersion.Activity)
                .Select(v => (long?)v.Version)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (PostgresException e) when (e.SqlState == UndefinedTable)
        {
            version = null;
        }

        if (version == store.Current?.Version)
            return false;

        var snapshot = version is null ? null : await LoadAsync(db, cancellationToken);
        store.Publish(snapshot);
        return true;
    }

    /// <summary>
    /// The version row and the tables change in one transaction on the intel side, so they are read in
    /// one snapshot here (through the execution strategy, which retries the whole read).
    /// </summary>
    private Task<ActivitySnapshot?> LoadAsync(IntelDbContext db, CancellationToken cancellationToken) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, cancellationToken);
            var version = await db.Versions.AsNoTracking().Where(v => v.Dataset == IntelVersion.Activity).Select(v => v.Version).FirstAsync(cancellationToken);
            var systems = await db.SystemActivityCurrent.AsNoTracking().ToDictionaryAsync(s => s.SystemId, cancellationToken);
            var regions = await db.RegionActivityCurrent.AsNoTracking().ToDictionaryAsync(r => r.RegionId, cancellationToken);
            var samples = await db.ActivityWeekSamples.AsNoTracking().ToDictionaryAsync(s => s.HourOfWeek, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            LogLoaded(version, systems.Count, regions.Count);
            return systems.Count > 0 ? new ActivitySnapshot(version, systems, regions, samples) : null;
        });

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded activity version {Version}: {Systems} systems, {Regions} regions")]
    private partial void LogLoaded(long version, int systems, int regions);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the intel service's activity tables")]
    private partial void LogFailed(Exception exception);
}

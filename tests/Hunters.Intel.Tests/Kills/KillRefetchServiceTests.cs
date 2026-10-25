using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Hunting;
using NewEdenSoft.Hunters.Intel.Kills;
using NewEdenSoft.Hunters.Intel.Sde;
using NewEdenSoft.Hunters.Intel.Tests.Hunting;

namespace NewEdenSoft.Hunters.Intel.Tests.Kills;

[Collection(IntelDatabaseCollection.Name)]
public sealed class KillRefetchServiceTests(IntelDatabase database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Newest = new(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);

    private readonly FakeKillmails _killmails = new();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _services = database.Services(new FakeSystemActivity(), o => o.Kills.Refetch.PerSecond = 1000, killmails: _killmails);
        await using var db = Db();
        await db.Kills.ExecuteDeleteAsync();
        await db.Sightings.ExecuteDeleteAsync();
        await db.HuntingProfileStates.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private IntelDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<IntelDbContext>();

    private KillRefetchService Service => _services.GetRequiredService<KillRefetchService>();

    /// <summary>A kill <paramref name="hoursAgo"/> before the newest, its victim pilot named after it; no counts unless given.</summary>
    private static Kill Stored(long id, int hoursAgo, int[] fitted, int[]? counts = null)
    {
        var kill = SightingMatcherTests.Kill(FakeSde.Rifter, victim: 90000000 + id, fitted: fitted, counts: counts ?? []);
        kill.KillmailId = id;
        kill.Hash = $"h{id}";
        kill.Time = Newest.AddHours(-hoursAgo);
        return kill;
    }

    /// <summary>Stores the kills, then resolves and backfills the profiles, as at startup after the upgrade.</summary>
    private async Task StartAsync(params Kill[] kills)
    {
        await using (var db = Db())
        {
            db.Kills.AddRange(kills);
            await db.SaveChangesAsync();
        }

        await _services.GetRequiredService<SdeCatalogService>().LoadAsync(CancellationToken.None);
        var catalog = _services.GetRequiredService<SdeCatalogStore>().Current!;
        await _services.GetRequiredService<HuntingProfileService>().SyncAsync(catalog, CancellationToken.None);
    }

    private async Task<long[]> SmartbombersAsync()
    {
        await using var db = Db();
        return [.. (await db.Sightings.Where(s => s.Profile == "smartbombers").Select(s => s.KillmailId).ToListAsync()).Order()];
    }

    [Fact]
    public async Task Only_kills_with_too_few_distinct_smart_bombs_to_tell_are_fetched_newest_first()
    {
        _killmails.Known[1] = new KillmailFittings([FakeSde.Smartbomb, 2873], [5, 1]); // A smartbomber after all.
        _killmails.Known[2] = new KillmailFittings([FakeSde.Smartbomb], [1]); // A utility smart bomb.
        // 3: gone from ESI.
        await StartAsync(
            Stored(3, hoursAgo: 3, [FakeSde.Smartbomb]),
            Stored(1, hoursAgo: 1, [FakeSde.Smartbomb, 2873]),
            Stored(2, hoursAgo: 2, [FakeSde.Smartbomb]),
            // Five distinct smart bombs: a smartbomber without asking.
            Stored(4, hoursAgo: 4, [FakeSde.SmallSmartbomb, FakeSde.ProtonSmartbomb, FakeSde.EmpSmartbomb, FakeSde.PlasmaSmartbomb, FakeSde.Smartbomb]),
            // From the feed, with its counts already.
            Stored(5, hoursAgo: 5, [FakeSde.Smartbomb], counts: [1]),
            // No smart bomb at all.
            Stored(6, hoursAgo: 6, [2873]));

        // The backfill at startup can only tell kill 4.
        Assert.Equal(new long[] { 4 }, await SmartbombersAsync());

        Assert.Equal(TimeSpan.FromHours(1), await Service.RunAsync(CancellationToken.None));

        Assert.Equal([1L, 2, 3], _killmails.Calls);
        Assert.Equal(new long[] { 1, 4 }, await SmartbombersAsync());
        await using (var db = Db())
        {
            var counts = await db.Kills.OrderBy(k => k.KillmailId).Select(k => k.VictimFittedCounts).ToListAsync();
            Assert.Equal([[5, 1], [1], [0], [], [1], []], counts);
        }

        // Nothing left: the next run asks ESI nothing.
        await Service.RunAsync(CancellationToken.None);
        Assert.Equal(3, _killmails.Calls.Count);
    }

    [Fact]
    public async Task Esi_asking_to_slow_down_pauses_the_run_and_the_kill_is_asked_again()
    {
        _killmails.Known[1] = new KillmailFittings([FakeSde.Smartbomb], [6]);
        _killmails.Next = new EsiThrottledException(TimeSpan.FromMinutes(2), "ESI's error limit is down to 10.");
        await StartAsync(Stored(1, hoursAgo: 1, [FakeSde.Smartbomb]));

        Assert.Equal(TimeSpan.FromMinutes(2), await Service.RunAsync(CancellationToken.None));
        Assert.Empty(await SmartbombersAsync());

        await Service.RunAsync(CancellationToken.None);
        Assert.Equal([1L, 1], _killmails.Calls);
        Assert.Equal(new long[] { 1 }, await SmartbombersAsync());
    }

    [Fact]
    public async Task Another_worker_holding_the_lock_is_left_to_it()
    {
        _killmails.Known[1] = new KillmailFittings([FakeSde.Smartbomb], [5]);
        await StartAsync(Stored(1, hoursAgo: 1, [FakeSde.Smartbomb]));

        await using (var other = Db())
        {
            await other.Database.OpenConnectionAsync();
            await other.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({KillRefetchService.AdvisoryLock})");

            Assert.Equal(TimeSpan.FromHours(1), await Service.RunAsync(CancellationToken.None));
            Assert.Empty(_killmails.Calls);

            await other.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({KillRefetchService.AdvisoryLock})");
        }

        // Released: this run does the work, and releases it in turn for the next.
        await Service.RunAsync(CancellationToken.None);
        Assert.Equal([1L], _killmails.Calls);
        await using var check = Db();
        await check.Database.OpenConnectionAsync();
        Assert.True(await check.Database.SqlQuery<bool>($"""SELECT pg_try_advisory_lock({KillRefetchService.AdvisoryLock}) AS "Value" """).SingleAsync());
        await check.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({KillRefetchService.AdvisoryLock})");
    }
}

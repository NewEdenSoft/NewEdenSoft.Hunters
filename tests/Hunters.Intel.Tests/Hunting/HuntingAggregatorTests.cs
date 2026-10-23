using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Hunting;
using NewEdenSoft.Hunters.Intel.Sde;

namespace NewEdenSoft.Hunters.Intel.Tests.Hunting;

[Collection(IntelDatabaseCollection.Name)]
public sealed class HuntingAggregatorTests(IntelDatabase database) : IAsyncLifetime
{
    private const int Unknown = 31000005;
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private ServiceProvider _services = null!;
    private long _kill;

    public async Task InitializeAsync()
    {
        _services = database.Services(new FakeSystemActivity(), time: _time);
        await _services.GetRequiredService<SdeCatalogService>().LoadAsync(CancellationToken.None);
        await using var db = Db();
        await db.Sightings.ExecuteDeleteAsync();
        await db.HuntingSystemStats.ExecuteDeleteAsync();
        await db.HuntingRegionStats.ExecuteDeleteAsync();
        await db.Versions.Where(v => v.Dataset == IntelVersion.Hunting).ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private IntelDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<IntelDbContext>();

    private Sighting Seen(string profile, DateTimeOffset time, long pilot = 0, double weight = 1, bool indicator = false, bool fleet = false, int system = FakeSde.Jita) => new()
    {
        Profile = profile,
        KillmailId = ++_kill,
        SystemId = system,
        Time = time,
        Hour = time,
        PilotId = pilot,
        Weight = weight,
        Indicator = indicator,
        IsFleet = fleet,
    };

    private async Task SeedAsync(params Sighting[] sightings)
    {
        await using var db = Db();
        db.Sightings.AddRange(sightings);
        await db.SaveChangesAsync();
    }

    private Task UpdateAsync() => _services.GetRequiredService<HuntingAggregator>().UpdateAsync(CancellationToken.None);

    private static double Fade(TimeSpan age) => Math.Pow(0.5, age.TotalDays / 14);

    [Fact]
    public async Task A_system_has_its_windows_score_pilots_and_hours_of_the_day()
    {
        await SeedAsync(
            Seen("carriers", Now.AddDays(-1).AddHours(-2), pilot: 1),      // 18:00, this week
            Seen("carriers", Now.AddDays(-10), pilot: 2),                    // 20:00, this month
            Seen("carriers", Now.AddDays(-2).AddHours(-2), weight: 0.75, indicator: true), // 18:00, a CRAB beacon
            Seen("carriers", Now.AddDays(-40).AddHours(-5), pilot: 3),       // 15:00, only in the score and hours
            Seen("carriers", Now.AddHours(-1), pilot: 4, fleet: true));      // a fleet fight: not counted

        await UpdateAsync();

        await using var db = Db();
        var jita = await db.HuntingSystemStats.SingleAsync();
        Assert.Equal(("carriers", FakeSde.Jita), (jita.Profile, jita.SystemId));
        Assert.Equal((1.75, 2.75, 0.75, 2), (jita.Week, jita.Month, jita.MonthIndicator, jita.Pilots));
        Assert.Equal(Now.AddDays(-1).AddHours(-2), jita.LastSeen);
        Assert.Equal(Fade(TimeSpan.FromDays(1.0 + 2 / 24.0)) + Fade(TimeSpan.FromDays(10)) + 0.75 * Fade(TimeSpan.FromDays(2.0 + 2 / 24.0)) + Fade(TimeSpan.FromDays(40.0 + 5 / 24.0)), jita.Score, 9);

        var hours = new double[24];
        hours[18] = 1.75;
        hours[20] = 1;
        hours[15] = 1;
        Assert.Equal(hours, jita.Hours);
    }

    [Fact]
    public async Task Regions_add_up_their_systems_and_unknown_systems_count_only_for_themselves()
    {
        await SeedAsync(
            Seen("crab", Now.AddHours(-3)),
            Seen("crab", Now.AddHours(-2), system: FakeSde.Perimeter),
            Seen("crab", Now.AddHours(-2), system: FakeSde.Sobaseki),
            Seen("crab", Now.AddHours(-2), system: Unknown),
            Seen("smartbombers", Now.AddHours(-1), pilot: 7));

        await UpdateAsync();

        await using var db = Db();
        Assert.Equal(5, await db.HuntingSystemStats.CountAsync());
        var regions = await db.HuntingRegionStats.OrderBy(r => r.Profile).ThenBy(r => r.RegionId).ToListAsync();
        Assert.Equal(
            [("crab", FakeSde.TheForge, 2.0), ("crab", FakeSde.Lonetrek, 1.0), ("smartbombers", FakeSde.TheForge, 1.0)],
            regions.Select(r => (r.Profile, r.RegionId, r.Week)));
        Assert.Equal(1, regions.Single(r => r.Profile == "smartbombers").Pilots);
    }

    [Fact]
    public async Task Each_update_replaces_the_aggregates_and_moves_the_version()
    {
        await SeedAsync(Seen("ishtars", Now.AddHours(-1), pilot: 1));
        await UpdateAsync();

        await using (var db = Db())
        {
            await db.Sightings.ExecuteDeleteAsync();
            Assert.Equal(1, (await db.Versions.SingleAsync(v => v.Dataset == IntelVersion.Hunting)).Version);
        }

        // A week later the sighting has left the week window; once deleted, the row goes too.
        _time.Advance(TimeSpan.FromDays(8));
        await UpdateAsync();

        await using (var db = Db())
        {
            Assert.Empty(await db.HuntingSystemStats.ToListAsync());
            Assert.Empty(await db.HuntingRegionStats.ToListAsync());
            var version = await db.Versions.SingleAsync(v => v.Dataset == IntelVersion.Hunting);
            Assert.Equal((2L, Now.AddDays(8)), (version.Version, version.UpdatedAt));
        }
    }

    [Fact]
    public async Task The_windows_move_with_the_clock()
    {
        await SeedAsync(Seen("marauders", Now.AddDays(-6), pilot: 1));
        await UpdateAsync();
        _time.Advance(TimeSpan.FromDays(2));
        await UpdateAsync();

        await using var db = Db();
        var row = await db.HuntingSystemStats.SingleAsync();
        Assert.Equal((0.0, 1.0), (row.Week, row.Month));
        Assert.Equal(Fade(TimeSpan.FromDays(8)), row.Score, 9);
    }
}

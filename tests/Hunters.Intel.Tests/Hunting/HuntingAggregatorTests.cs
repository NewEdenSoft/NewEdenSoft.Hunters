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
        await db.HuntingAppearances.ExecuteDeleteAsync();
        await db.HuntingSystemStats.ExecuteDeleteAsync();
        await db.HuntingRegionStats.ExecuteDeleteAsync();
        await db.Versions.Where(v => v.Dataset == IntelVersion.Hunting).ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private IntelDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<IntelDbContext>();

    private Sighting Seen(
        string profile, DateTimeOffset time, long pilot = 0, double weight = 1, bool indicator = false, bool fleet = false, int system = FakeSde.Jita,
        bool local = false) => new()
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
        Local = local,
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
    public async Task Regulars_are_seen_on_different_days_at_about_the_same_time_of_day()
    {
        static DateTimeOffset At(int day, int hour) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);
        await SeedAsync(
            // Late evenings into the night: one four-hour window across midnight, on three days.
            Seen("carriers", At(22, 22), pilot: 1), Seen("carriers", At(20, 23), pilot: 1), Seen("carriers", At(18, 1), pilot: 1),
            // Five hours apart: only the extended window holds both days.
            Seen("carriers", At(20, 14), pilot: 2), Seen("carriers", At(21, 19), pilot: 2),
            // Twice on one day: no routine yet.
            Seen("carriers", At(22, 18), pilot: 3), Seen("carriers", At(22, 21), pilot: 3),
            // One of the days was a fleet fight, or older than the month.
            Seen("carriers", At(21, 18), pilot: 4), Seen("carriers", At(22, 18), pilot: 4, fleet: true),
            Seen("carriers", At(21, 18), pilot: 5), Seen("carriers", Now.AddDays(-31), pilot: 5),
            // Another profile is its own regular; seen once as a local.
            Seen("ishtars", At(21, 10), pilot: 1, local: true), Seen("ishtars", At(22, 11), pilot: 1));

        await UpdateAsync();

        await using var db = Db();
        var regulars = await db.HuntingRegulars.OrderBy(r => r.Profile).ThenBy(r => r.PilotId).ToListAsync();
        Assert.Equal(
            [
                ("carriers", 1L, 3, 3, 22, 4, At(22, 22), false),
                ("carriers", 2L, 2, 2, 14, 6, At(21, 19), false),
                // Seen at 10:00 and 11:00: the window starts when they show up.
                ("ishtars", 1L, 2, 2, 10, 4, At(22, 11), true),
            ],
            regulars.Select(r => (r.Profile, r.PilotId, r.Days, r.DaysInWindow, r.WindowStart, r.WindowHours, r.LastSeen, r.Local)));
        Assert.All(regulars, r => Assert.Equal(FakeSde.Jita, r.SystemId));

        // Strength: each day in the window, fading from its latest sighting there; the extended window at half.
        var night = Fade(Now - At(22, 22)) + Fade(Now - At(20, 23)) + Fade(Now - At(18, 1));
        var afternoon = 0.5 * (Fade(Now - At(20, 14)) + Fade(Now - At(21, 19)));
        var morning = Fade(Now - At(21, 10)) + Fade(Now - At(22, 11));
        Assert.Equal([night, afternoon, morning], regulars.Select(r => r.Strength), new DoubleComparer(9));

        // Reliable: per hour of the day, the regulars whose window covers it.
        var carriers = await db.HuntingSystemStats.SingleAsync(s => s.Profile == "carriers" && s.SystemId == FakeSde.Jita);
        var expected = new double[24];
        foreach (var hour in new[] { 22, 23, 0, 1 })
            expected[hour] = night;
        for (var hour = 14; hour < 20; hour++)
            expected[hour] = afternoon;
        Assert.Equal(expected, carriers.Reliable, new DoubleComparer(9));
        var ishtars = await db.HuntingSystemStats.SingleAsync(s => s.Profile == "ishtars");
        Assert.Equal([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, morning, morning, morning, morning, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], ishtars.Reliable, new DoubleComparer(9));
    }

    [Fact]
    public async Task A_known_smartbombers_appearances_on_its_alts_losses_make_it_a_regular()
    {
        static DateTimeOffset At(int day, int hour) => new(2026, 10, day, hour, 0, 0, TimeSpan.Zero);
        HuntingAppearance Appeared(long pilot, DateTimeOffset time, bool fleet = false) =>
            new() { Profile = "smartbombers", KillmailId = ++_kill, SystemId = FakeSde.Jita, Time = time, PilotId = pilot, IsFleet = fleet };

        // Pilot 1 is a known smartbomber in Jita (friendly fire once); pilot 2 only ever shows on others' losses.
        await SeedAsync(Seen("smartbombers", At(22, 22), pilot: 1));
        await using (var db = Db())
        {
            db.HuntingAppearances.AddRange(
                Appeared(1, At(20, 23)), Appeared(1, At(18, 22), fleet: true),
                Appeared(2, At(20, 22)), Appeared(2, At(21, 22)), Appeared(2, At(22, 22)));
            await db.SaveChangesAsync();
        }

        await UpdateAsync();

        await using var check = Db();
        var regular = Assert.Single(await check.HuntingRegulars.ToListAsync());
        Assert.Equal((1L, 2, 2, 22, 4), (regular.PilotId, regular.Days, regular.DaysInWindow, regular.WindowStart, regular.WindowHours));
        Assert.Equal(Fade(Now - At(22, 22)) + Fade(Now - At(20, 23)), regular.Strength, 9);
        // The scores are the sightings' alone.
        var stats = await check.HuntingSystemStats.SingleAsync();
        Assert.Equal((1.0, 1), (stats.Month, stats.Pilots));
    }

    [Fact]
    public async Task A_system_without_regulars_is_reliable_at_no_hour()
    {
        await SeedAsync(Seen("carriers", Now.AddDays(-1), pilot: 1));

        await UpdateAsync();

        await using var db = Db();
        Assert.Equal(new double[24], (await db.HuntingSystemStats.SingleAsync()).Reliable);
    }

    private sealed class DoubleComparer(int precision) : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Round(x - y, precision) == 0;

        public int GetHashCode(double obj) => 0;
    }

    [Fact]
    public async Task Locals_are_counted_apart_within_the_month()
    {
        await SeedAsync(
            Seen("ishtars", Now.AddDays(-1), pilot: 1, weight: 1.5, local: true),
            Seen("ishtars", Now.AddDays(-2), pilot: 1, weight: 1.5, local: true),
            Seen("ishtars", Now.AddDays(-3), pilot: 2),
            Seen("ishtars", Now.AddDays(-40), pilot: 3, weight: 1.5, local: true)); // Older than the month.

        await UpdateAsync();

        await using var db = Db();
        var jita = await db.HuntingSystemStats.SingleAsync();
        Assert.Equal((4.0, 3.0, 2, 1), (jita.Month, jita.MonthLocal, jita.Pilots, jita.LocalPilots));
        var forge = await db.HuntingRegionStats.SingleAsync();
        Assert.Equal((3.0, 1), (forge.MonthLocal, forge.LocalPilots));
    }

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

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Sde;

namespace NewEdenSoft.Hunters.Intel.Tests.Activity;

[Collection(IntelDatabaseCollection.Name)]
public sealed class ActivityAggregatorTests(IntelDatabase database) : IAsyncLifetime
{
    private const int Jita = FakeSde.Jita, Perimeter = FakeSde.Perimeter, Sobaseki = FakeSde.Sobaseki;

    /// <summary>A wormhole system the SDE fake doesn't know: counted for itself, in no region.</summary>
    private const int Unknown = 31000005;

    /// <summary>Saturday 17 October 2026, 20:00:48 UTC: the end of an ESI hour covering Saturday 19:00 (hour of week 139).</summary>
    private static readonly DateTimeOffset Saturday20 = new(2026, 10, 17, 20, 0, 48, TimeSpan.Zero);

    private const short Saturday19 = 5 * 24 + 19;

    private readonly FakeTimeProvider _time = new(Saturday20.AddMinutes(5));
    private ServiceProvider _services = null!;

    public Task InitializeAsync() => StartAsync();

    private async Task StartAsync(Action<IntelOptions>? configure = null)
    {
        if (_services is not null)
            await _services.DisposeAsync();
        _services = database.Services(new FakeSystemActivity(), configure, time: _time);
        await _services.GetRequiredService<SdeCatalogService>().LoadAsync(CancellationToken.None);

        // Tests share one database: start empty.
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        await db.SystemKills.ExecuteDeleteAsync();
        await db.SystemJumps.ExecuteDeleteAsync();
        await db.SystemActivityWeek.ExecuteDeleteAsync();
        await db.RegionActivityWeek.ExecuteDeleteAsync();
        await db.ActivityWeekSamples.ExecuteDeleteAsync();
        await db.SystemActivityCurrent.ExecuteDeleteAsync();
        await db.RegionActivityCurrent.ExecuteDeleteAsync();
        await db.Versions.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private ActivityAggregator Aggregator => _services.GetRequiredService<ActivityAggregator>();

    private static SystemKillsSnapshot Kills(DateTimeOffset end, params (int Id, int Ship, int Pod, int Npc)[] systems) => new()
    {
        PeriodEnd = end,
        FetchedAt = end,
        SystemIds = [.. systems.Select(s => s.Id)],
        ShipKills = [.. systems.Select(s => s.Ship)],
        PodKills = [.. systems.Select(s => s.Pod)],
        NpcKills = [.. systems.Select(s => s.Npc)],
    };

    private static SystemJumpsSnapshot Jumps(DateTimeOffset end, params (int Id, int Jumps)[] systems) => new()
    {
        PeriodEnd = end,
        FetchedAt = end,
        SystemIds = [.. systems.Select(s => s.Id)],
        ShipJumps = [.. systems.Select(s => s.Jumps)],
    };

    private async Task SeedAsync(params object[] hours)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        db.AddRange(hours);
        await db.SaveChangesAsync();
    }

    private async Task<T> QueryAsync<T>(Func<IntelDbContext, Task<T>> query)
    {
        await using var scope = _services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IntelDbContext>());
    }

    private Task<SystemActivityCurrent> SystemAsync(int id) =>
        QueryAsync(db => db.SystemActivityCurrent.SingleAsync(c => c.SystemId == id));

    [Fact]
    public async Task Adds_each_hour_to_its_hour_of_week_and_counts_the_samples()
    {
        // Two Saturdays at 19:00, a week apart, and the hour before.
        await SeedAsync(
            Kills(Saturday20, (Jita, 2, 1, 30), (Perimeter, 0, 0, 5)),
            Kills(Saturday20.AddDays(-7), (Jita, 1, 0, 10)),
            Kills(Saturday20.AddHours(-1), (Jita, 0, 0, 4)),
            Jumps(Saturday20, (Jita, 1000)),
            Jumps(Saturday20.AddDays(-7), (Jita, 800)));

        Assert.True(await Aggregator.UpdateAsync(CancellationToken.None));

        var week = await QueryAsync(db => db.SystemActivityWeek.Where(w => w.SystemId == Jita).OrderBy(w => w.HourOfWeek).ToListAsync());
        Assert.Equal([(short)(Saturday19 - 1), Saturday19], week.Select(w => w.HourOfWeek));
        Assert.Equal((40L, 3L, 1L, 1800L), (week[1].NpcKills, week[1].ShipKills, week[1].PodKills, week[1].ShipJumps));
        Assert.Equal((4L, 0L), (week[0].NpcKills, week[0].ShipJumps));

        var samples = await QueryAsync(db => db.ActivityWeekSamples.SingleAsync(s => s.HourOfWeek == Saturday19));
        Assert.Equal((2, 2), (samples.KillHours, samples.JumpHours));
        Assert.All(await QueryAsync(db => db.Set<SystemKillsSnapshot>().ToListAsync()), k => Assert.True(k.Applied));
    }

    [Fact]
    public async Task Regions_are_the_sum_of_their_systems()
    {
        await SeedAsync(
            Kills(Saturday20, (Jita, 2, 1, 30), (Perimeter, 1, 0, 5), (Sobaseki, 0, 0, 7), (Unknown, 9, 9, 9)),
            Jumps(Saturday20, (Jita, 1000), (Perimeter, 400), (Unknown, 50)));

        await Aggregator.UpdateAsync(CancellationToken.None);

        var forge = await QueryAsync(db => db.RegionActivityWeek.SingleAsync(r => r.RegionId == FakeSde.TheForge));
        Assert.Equal((35L, 3L, 1L, 1400L), (forge.NpcKills, forge.ShipKills, forge.PodKills, forge.ShipJumps));
        Assert.Equal(7, (await QueryAsync(db => db.RegionActivityWeek.SingleAsync(r => r.RegionId == FakeSde.Lonetrek))).NpcKills);
        Assert.Equal(2, await QueryAsync(db => db.RegionActivityWeek.CountAsync()));

        var current = await QueryAsync(db => db.RegionActivityCurrent.SingleAsync(r => r.RegionId == FakeSde.TheForge));
        Assert.Equal((35, 1400), (current.NpcKills.LastHour, current.ShipJumps.LastHour));

        // The unknown system still has its own row.
        Assert.Equal(9, (await SystemAsync(Unknown)).NpcKills.LastHour);
    }

    [Fact]
    public async Task Hours_past_the_retention_are_taken_out_of_the_sums_and_deleted()
    {
        await StartAsync(o => o.Activity.Retention = TimeSpan.FromDays(3));
        await SeedAsync(
            Kills(Saturday20.AddDays(-2), (Jita, 0, 0, 10), (Perimeter, 0, 0, 3)),
            Kills(Saturday20.AddHours(-1), (Jita, 0, 0, 4)),
            Jumps(Saturday20.AddDays(-2), (Jita, 100)));
        await Aggregator.UpdateAsync(CancellationToken.None);

        // Two days later, Thursday's hour is past the retention.
        _time.Advance(TimeSpan.FromDays(2));
        Assert.True(await Aggregator.UpdateAsync(CancellationToken.None));

        Assert.Equal([Saturday20.AddHours(-1)], await QueryAsync(db => db.SystemKills.Select(k => k.PeriodEnd).ToListAsync()));
        Assert.Empty(await QueryAsync(db => db.SystemJumps.ToListAsync()));

        var week = await QueryAsync(db => db.SystemActivityWeek.ToListAsync());
        var only = Assert.Single(week);
        Assert.Equal((Jita, (short)(Saturday19 - 1), 4L), (only.SystemId, only.HourOfWeek, only.NpcKills));
        Assert.Single(await QueryAsync(db => db.RegionActivityWeek.ToListAsync()));

        var samples = await QueryAsync(db => db.ActivityWeekSamples.OrderBy(s => s.HourOfWeek).ToListAsync());
        Assert.All(samples.Where(s => s.HourOfWeek != Saturday19 - 1), s => Assert.Equal((0, 0), (s.KillHours, s.JumpHours)));
        Assert.Equal(1, samples.Single(s => s.HourOfWeek == Saturday19 - 1).KillHours);
    }

    [Fact]
    public async Task Current_has_the_latest_hour_the_one_before_and_the_last_24_hours()
    {
        // 26 hourly hours, Jita with k + 1 NPC kills k hours back; jumps only for the latest hour.
        await SeedAsync([
            .. Enumerable.Range(0, 26).Select(k => Kills(Saturday20.AddHours(-k), (Jita, 0, 0, k + 1))),
            Jumps(Saturday20, (Jita, 500)),
        ]);

        await Aggregator.UpdateAsync(CancellationToken.None);

        var jita = await SystemAsync(Jita);
        Assert.Equal(1, jita.NpcKills.LastHour);
        Assert.Equal(2, jita.NpcKills.PreviousHour);
        Assert.Equal(Enumerable.Range(1, 24).Sum(), jita.NpcKills.Day);
        Assert.Equal(((short)24, (short)1), (jita.KillsDayHours, jita.JumpsDayHours));
        Assert.Equal((Saturday20, Saturday20), (jita.KillsPeriodEnd, jita.JumpsPeriodEnd));
        Assert.Equal(500, jita.ShipJumps.LastHour);
        Assert.Null(jita.ShipJumps.PreviousHour);

        // One sample per bucket: usual for the latest hour's bucket is that hour itself.
        Assert.Equal(1, jita.NpcKills.Typical);
    }

    [Fact]
    public async Task There_is_no_previous_hour_across_a_gap()
    {
        await SeedAsync(
            Kills(Saturday20, (Jita, 0, 0, 5)),
            Kills(Saturday20.AddHours(-2), (Jita, 0, 0, 9)));

        await Aggregator.UpdateAsync(CancellationToken.None);

        var jita = await SystemAsync(Jita);
        Assert.Null(jita.NpcKills.PreviousHour);
        Assert.Equal((14, (short)2), (jita.NpcKills.Day, jita.KillsDayHours));
    }

    [Fact]
    public async Task A_system_quiet_in_the_latest_hour_shows_zero_and_its_previous_hour()
    {
        await SeedAsync(
            Kills(Saturday20, (Jita, 0, 0, 5)),
            Kills(Saturday20.AddHours(-1), (Perimeter, 0, 0, 3)));

        await Aggregator.UpdateAsync(CancellationToken.None);

        var perimeter = await SystemAsync(Perimeter);
        Assert.Equal((0, (int?)3, 3), (perimeter.NpcKills.LastHour, perimeter.NpcKills.PreviousHour, perimeter.NpcKills.Day));
    }

    /// <summary>A week of hours ending at <paramref name="latest"/>: Jita has 10 NPC kills an hour, 100 on Saturdays at 19:00.</summary>
    private Task SeedWeekAsync(DateTimeOffset latest, int hours = HourOfWeek.Count) =>
        SeedAsync([
            .. Enumerable.Range(0, hours).Select(k =>
            {
                var end = latest.AddHours(-k);
                return Kills(end, (Jita, 0, 0, HourOfWeek.Covered(end) == Saturday19 ? 100 : 10));
            }),
        ]);

    [Theory]
    [InlineData(19, 0, 1.0)] // Data up to 18:59, so "now" is 19:00: the peak.
    [InlineData(17, 2, 0.1)] // "Now" is 17:00: the peak is two hours away.
    public async Task Peak_now_compares_the_hour_now_with_the_busiest_hour(int latestHour, int hoursToPeak, double ratio)
    {
        await StartAsync(o => o.Activity.PeakMinWeeks = 1);
        await SeedWeekAsync(new DateTimeOffset(2026, 10, 17, latestHour, 0, 48, TimeSpan.Zero));

        await Aggregator.UpdateAsync(CancellationToken.None);

        var npc = (await SystemAsync(Jita)).NpcKills;
        Assert.Equal(ratio, npc.PeakRatio!.Value, 9);
        Assert.Equal((short)hoursToPeak, npc.HoursToPeak);
        Assert.Equal(Saturday19, npc.PeakHourOfWeek);
        Assert.Equal(10, npc.Typical);
    }

    [Fact]
    public async Task There_is_no_peak_without_enough_samples_for_every_hour_of_the_week()
    {
        await StartAsync(o => o.Activity.PeakMinWeeks = 1);
        await SeedWeekAsync(Saturday20.AddHours(-1), hours: HourOfWeek.Count - 1);

        await Aggregator.UpdateAsync(CancellationToken.None);

        var npc = (await SystemAsync(Jita)).NpcKills;
        Assert.Equal(10, npc.Typical);
        Assert.Null(npc.PeakRatio);
        Assert.Null(npc.HoursToPeak);
        Assert.Null(npc.PeakHourOfWeek);
    }

    [Fact]
    public async Task There_is_no_peak_for_a_quiet_system()
    {
        await StartAsync(o =>
        {
            o.Activity.PeakMinWeeks = 1;
            o.Activity.PeakMinAverage = 200;
        });
        await SeedWeekAsync(Saturday20.AddHours(-1));

        await Aggregator.UpdateAsync(CancellationToken.None);

        Assert.Null((await SystemAsync(Jita)).NpcKills.PeakRatio);
    }

    [Fact]
    public async Task The_version_moves_with_every_change_and_only_then()
    {
        await SeedAsync(Kills(Saturday20.AddHours(-1), (Jita, 0, 0, 1)));
        Assert.True(await Aggregator.UpdateAsync(CancellationToken.None));
        var first = await QueryAsync(db => db.Versions.SingleAsync(v => v.Dataset == IntelVersion.Activity));

        Assert.False(await Aggregator.UpdateAsync(CancellationToken.None));
        Assert.Equal(first.Version, (await QueryAsync(db => db.Versions.SingleAsync())).Version);

        await SeedAsync(Kills(Saturday20, (Jita, 0, 0, 2)));
        Assert.True(await Aggregator.UpdateAsync(CancellationToken.None));
        Assert.Equal(first.Version + 1, (await QueryAsync(db => db.Versions.SingleAsync())).Version);
    }

    [Fact]
    public async Task Updating_hour_by_hour_gives_the_same_tables_as_a_rebuild()
    {
        await StartAsync(o =>
        {
            o.Activity.Retention = TimeSpan.FromDays(4);
            o.Activity.PeakMinWeeks = 0;
        });
        var random = new Random(22);
        int[] systems = [Jita, Perimeter, Sobaseki, Unknown];
        var start = Saturday20.AddDays(1);
        _time.SetUtcNow(start);

        // Ten days of hours with gaps, applied in batches while hours expire behind them.
        for (var hour = 0; hour < 240; hour++)
        {
            var end = start.AddHours(hour);
            var picked = systems.Where(_ => random.Next(3) > 0).ToArray();
            if (random.Next(10) > 0)
                await SeedAsync(Kills(end, [.. picked.Select(s => (s, random.Next(3), random.Next(2), random.Next(50)))]));
            if (random.Next(10) > 0)
                await SeedAsync(Jumps(end, [.. picked.Select(s => (s, random.Next(2000)))]));

            if (random.Next(6) == 0)
            {
                _time.SetUtcNow(end.AddMinutes(1));
                await Aggregator.UpdateAsync(CancellationToken.None);
            }
        }

        _time.SetUtcNow(start.AddHours(240));
        await Aggregator.UpdateAsync(CancellationToken.None);
        var incremental = await DerivedAsync();

        await Aggregator.RebuildAsync(CancellationToken.None);

        Assert.Equal(incremental, await DerivedAsync());
        Assert.Matches("\"PeakRatio\":[0-9]", incremental);
        Assert.InRange(await QueryAsync(db => db.SystemKills.CountAsync()), 1, 4 * 24 + 1);
    }

    /// <summary>Every derived table, as JSON in key order, with all-zero sample rows left out.</summary>
    private Task<string> DerivedAsync() => QueryAsync(async db => JsonSerializer.Serialize(new
    {
        Systems = await db.SystemActivityWeek.OrderBy(w => w.SystemId).ThenBy(w => w.HourOfWeek).ToListAsync(),
        Regions = await db.RegionActivityWeek.OrderBy(w => w.RegionId).ThenBy(w => w.HourOfWeek).ToListAsync(),
        Samples = await db.ActivityWeekSamples.Where(s => s.KillHours != 0 || s.JumpHours != 0).OrderBy(s => s.HourOfWeek).ToListAsync(),
        SystemsNow = await db.SystemActivityCurrent.OrderBy(c => c.SystemId).ToListAsync(),
        RegionsNow = await db.RegionActivityCurrent.OrderBy(c => c.RegionId).ToListAsync(),
    }));
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Hunting;
using NewEdenSoft.Hunters.Intel.Sde;

namespace NewEdenSoft.Hunters.Intel.Tests.Hunting;

[Collection(IntelDatabaseCollection.Name)]
public sealed class SightingStoreTests(IntelDatabase database) : IAsyncLifetime
{
    private const long Pilot = 90000001, Other = 90000002;
    private static readonly DateTimeOffset Time = new(2026, 10, 23, 5, 10, 0, TimeSpan.Zero);

    private ServiceProvider _services = null!;
    private List<ResolvedProfile> _profiles = null!;

    public async Task InitializeAsync()
    {
        _services = database.Services(new FakeSystemActivity());
        _profiles = await SightingMatcherTests.ProfilesAsync();
        await using var db = Db();
        await db.Kills.ExecuteDeleteAsync();
        await db.Sightings.ExecuteDeleteAsync();
        await db.HuntingProfileStates.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private IntelDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<IntelDbContext>();

    private static Kill Carrier(long id, DateTimeOffset time, long pilot = Pilot, int system = FakeSde.Jita, bool npc = false)
    {
        var kill = SightingMatcherTests.Kill(FakeSde.Thanatos, victim: pilot);
        kill.KillmailId = id;
        kill.Hash = $"h{id}";
        kill.Time = time;
        kill.SystemId = system;
        kill.Npc = npc;
        return kill;
    }

    private static Kill Frigate(long id, DateTimeOffset time, int system = FakeSde.Jita)
    {
        var kill = Carrier(id, time, system: system);
        kill.VictimShipTypeId = FakeSde.Rifter;
        return kill;
    }

    private async Task RecordAsync(params Kill[] kills)
    {
        await using var db = Db();
        await SightingStore.InsertAsync(db, [.. kills.SelectMany(k => SightingMatcher.Match(k, _profiles, 25))], CancellationToken.None);
    }

    private async Task<List<Sighting>> SightingsAsync()
    {
        await using var db = Db();
        return await db.Sightings.OrderBy(s => s.Time).ThenBy(s => s.Id).ToListAsync();
    }

    [Fact]
    public async Task A_pilot_counts_once_an_hour_and_a_kill_recorded_twice_counts_once()
    {
        await RecordAsync(Carrier(1, Time), Carrier(2, Time.AddMinutes(20)), Carrier(3, Time.AddMinutes(60)));
        await RecordAsync(Carrier(1, Time), Carrier(4, Time, pilot: Other));

        var sightings = await SightingsAsync();
        Assert.Equal([(1L, Pilot), (4L, Other), (3L, Pilot)], sightings.Select(s => (s.KillmailId, s.PilotId)));
    }

    [Fact]
    public async Task Sightings_in_a_burst_of_kills_are_marked_as_a_battle_around_a_new_kill()
    {
        // A carrier seen as the fight starts, 20 kills within ten minutes, and the carrier back an hour later.
        await RecordAsync(Carrier(1, Time), Carrier(2, Time.AddHours(1).AddMinutes(10)));
        await using (var db = Db())
        {
            db.Kills.AddRange(Enumerable.Range(0, 20).Select(i => Frigate(100 + i, Time.AddMinutes(1 + i / 2.0))));
            db.Kills.Add(Frigate(200, Time.AddMinutes(5), system: FakeSde.Perimeter));
            await db.SaveChangesAsync();

            await SightingStore.MarkBattlesAsync(db, new HuntingOptions(), FakeSde.Jita, Time.AddMinutes(10), null, [], CancellationToken.None);
        }

        Assert.Equal([true, false], (await SightingsAsync()).Select(s => s.IsFleet));
    }

    [Fact]
    public async Task A_smartbomb_camp_keeps_its_burst_of_pod_kills()
    {
        // 20 pods smartbombed within ten minutes: a battle by the rule, but the camp is the target.
        var pods = Enumerable.Range(0, 20).Select(i =>
        {
            var pod = SightingMatcherTests.Kill(FakeSde.Capsule, victim: 91000000 + i, attackers: [(Pilot, FakeSde.Rifter, FakeSde.Smartbomb)]);
            pod.KillmailId = 300 + i;
            pod.Hash = $"h{300 + i}";
            pod.Time = Time.AddSeconds(i * 30);
            return pod;
        }).ToArray();
        await RecordAsync(pods);
        await using (var db = Db())
        {
            db.Kills.AddRange(pods);
            await db.SaveChangesAsync();
            await SightingStore.MarkBattlesAsync(db, new HuntingOptions(), FakeSde.Jita, Time.AddMinutes(5), null, ["smartbomb-camps"], CancellationToken.None);
        }

        var sightings = await SightingsAsync();
        Assert.False(Assert.Single(sightings, s => s.Profile == "smartbomb-camps").IsFleet);
        Assert.True(Assert.Single(sightings, s => s.Profile == "smartbombers").IsFleet);
    }

    [Fact]
    public async Task Npc_kills_do_not_make_a_battle()
    {
        await RecordAsync(Carrier(1, Time));
        await using (var db = Db())
        {
            db.Kills.AddRange(Enumerable.Range(0, 30).Select(i => Carrier(100 + i, Time.AddSeconds(i * 10), pilot: 0, npc: true)));
            await db.SaveChangesAsync();
            await SightingStore.MarkBattlesAsync(db, new HuntingOptions(), FakeSde.Jita, Time, null, [], CancellationToken.None);
        }

        Assert.False(Assert.Single(await SightingsAsync()).IsFleet);
    }

    private HuntingProfileService Profiles => _services.GetRequiredService<HuntingProfileService>();

    private async Task<SdeCatalog> CatalogAsync()
    {
        await _services.GetRequiredService<SdeCatalogService>().LoadAsync(CancellationToken.None);
        return _services.GetRequiredService<SdeCatalogStore>().Current!;
    }

    [Fact]
    public async Task New_and_changed_profiles_are_backfilled_from_the_stored_kills_and_removed_ones_cleared()
    {
        await using (var db = Db())
        {
            db.Kills.AddRange(Carrier(1, Time), Carrier(2, Time.AddHours(2), pilot: Other));
            var crab = Frigate(3, Time);
            crab.VictimShipTypeId = FakeSde.CrabBeacon;
            db.Kills.Add(crab);
            await db.SaveChangesAsync();
        }

        var catalog = await CatalogAsync();
        await Profiles.SyncAsync(catalog, CancellationToken.None);

        var first = await SightingsAsync();
        Assert.Equal(
            ["carriers 0", "carriers 90000001", "crab 0", "supers 0", "carriers 90000002"],
            first.OrderBy(s => s.Time).ThenBy(s => s.Profile).ThenBy(s => s.PilotId).Select(s => $"{s.Profile} {s.PilotId}"));
        await using (var db = Db())
        {
            var states = await db.HuntingProfileStates.ToDictionaryAsync(s => s.Profile, s => s.Name);
            Assert.Equal(HuntingProfiles.All.ToDictionary(p => p.Id, p => p.Name), states);
        }

        // Nothing changed: nothing is rebuilt.
        await Profiles.SyncAsync(catalog, CancellationToken.None);
        Assert.Equal(first.Select(s => s.Id), (await SightingsAsync()).Select(s => s.Id));

        // The carrier profile changed, and an old profile is gone.
        await using (var db = Db())
        {
            var carriers = await db.HuntingProfileStates.SingleAsync(s => s.Profile == "carriers");
            carriers.Definition = "older rules";
            db.HuntingProfileStates.Add(new HuntingProfileState { Profile = "titans", Definition = "x" });
            db.Sightings.Add(new Sighting { Profile = "titans", KillmailId = 1, PilotId = Pilot, Time = Time, Hour = Time });
            await db.SaveChangesAsync();
        }

        await Profiles.SyncAsync(catalog, CancellationToken.None);

        var after = await SightingsAsync();
        Assert.DoesNotContain(after, s => s.Profile == "titans");
        Assert.Equal(first.Where(s => s.Profile != "carriers").Select(s => s.Id).Order(), after.Where(s => s.Profile != "carriers").Select(s => s.Id).Order());
        Assert.Equal(3, after.Count(s => s.Profile == "carriers"));
        Assert.DoesNotContain(after, s => first.Any(f => f.Profile == "carriers" && f.Id == s.Id));
        await using (var db = Db())
            Assert.Null(await db.HuntingProfileStates.FindAsync("titans"));
    }
}

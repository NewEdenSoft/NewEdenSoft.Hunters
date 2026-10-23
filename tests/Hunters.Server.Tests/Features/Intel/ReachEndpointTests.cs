using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Contracts.Intel;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Sde;
using NewEdenSoft.Hunters.Server.Features.Activity;
using NewEdenSoft.Hunters.Server.Features.Hunting;
using NewEdenSoft.Hunters.Server.Features.Intel;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Tests.Features.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Intel;

[Collection(PostgresCollection.Name)]
public sealed class ReachEndpointTests : IDisposable
{
    private const double Ly = FakeSdeMap.Ly;
    private const int Region = 10000001;
    private const int Origin = 30000001, HighSecNeighbour = 30000002, NullSecTwoJumps = 30000003, FarCyno = 30000004, FarPartner = 30000005;

    private readonly HuntersServerFactory _factory;
    private readonly int _hour = DateTimeOffset.UtcNow.Hour;

    public ReachEndpointTests(PostgresFixture postgres)
    {
        _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

        // Origin – high-sec neighbour (5 ly) – null-sec (12 ly, two jumps); a null-sec cyno target 7 ly away whose
        // only gate leads to a system 30 ly out.
        _factory.Sde.Data = new SdeMapData(
            FakeSdeMap.Build,
            [new SdeRegion(Region, "Test Region")],
            [
                new SdeSolarSystem(Origin, "Origin", Region, 20000001, 0.3, 0, 0, 0, null, null),
                new SdeSolarSystem(HighSecNeighbour, "Neighbour", Region, 20000001, 0.9, 5 * Ly, 0, 0, null, null),
                new SdeSolarSystem(NullSecTwoJumps, "Nullsec", Region, 20000001, -0.2, 12 * Ly, 0, 0, null, null),
                new SdeSolarSystem(FarCyno, "Far", Region, 20000001, -0.5, 0, 7 * Ly, 0, null, null),
                new SdeSolarSystem(FarPartner, "Further", Region, 20000001, -0.5, 0, 30 * Ly, 0, null, null),
            ],
            [
                new SdeStargate(Origin, HighSecNeighbour), new SdeStargate(HighSecNeighbour, Origin),
                new SdeStargate(HighSecNeighbour, NullSecTwoJumps), new SdeStargate(NullSecTwoJumps, HighSecNeighbour),
                new SdeStargate(FarCyno, FarPartner), new SdeStargate(FarPartner, FarCyno),
            ]);
    }

    public void Dispose() => _factory.Dispose();

    private static SystemActivityCurrent Npc(int system, int last, int previous) => new()
    {
        SystemId = system,
        NpcKills = new ActivityMetric { LastHour = last, PreviousHour = previous, Day = last * 10, Typical = 12.5, PeakRatio = 0.9, HoursToPeak = 0 },
        ShipKills = new ActivityMetric { PreviousHour = 0 },
        PodKills = new ActivityMetric { PreviousHour = 0 },
        ShipJumps = new ActivityMetric { LastHour = 100, PreviousHour = 90 },
    };

    private double[] HoursAt(int hour, double weight)
    {
        var hours = new double[24];
        hours[hour % 24] = weight;
        return hours;
    }

    /// <summary>
    /// NPC kills rising most at the far cyno target (+40) and the null-sec system (+30); carriers in the
    /// null-sec system this hour (likely now), and more often at the far target but at another hour.
    /// </summary>
    private void PublishIntel()
    {
        _factory.Services.GetRequiredService<ActivityStore>().Publish(new ActivitySnapshot(
            3,
            new Dictionary<int, SystemActivityCurrent> { [Origin] = Npc(Origin, 10, 10), [NullSecTwoJumps] = Npc(NullSecTwoJumps, 50, 20), [FarCyno] = Npc(FarCyno, 40, 0) },
            new Dictionary<int, RegionActivityCurrent>(),
            new Dictionary<short, ActivityWeekSamples>()));

        double[] nowAndLater = HoursAt(_hour, 2);
        nowAndLater[(_hour + 6) % 24] = 2;
        _factory.Services.GetRequiredService<HuntingStore>().Publish(new HuntingSnapshot(
            5,
            DateTimeOffset.UtcNow,
            [new HuntingProfileInfo("carriers", "Carriers"), new HuntingProfileInfo("crab", "CRAB beacons")],
            [
                new HuntingSystemStats { Profile = "carriers", SystemId = NullSecTwoJumps, Score = 4, LastSeen = DateTimeOffset.UtcNow, Hours = nowAndLater },
                new HuntingSystemStats { Profile = "carriers", SystemId = FarCyno, Score = 10, LastSeen = DateTimeOffset.UtcNow, Hours = HoursAt(_hour + 12, 5) },
            ],
            []));
    }

    private async Task<HttpClient> ReadyAsync()
    {
        var store = _factory.Services.GetRequiredService<MapPackStore>();
        for (var i = 0; store.Current is null && i < 200; i++)
            await Task.Delay(25);
        Assert.NotNull(store.Current);

        var driver = new LoginDriver(_factory);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Scout"));
        return driver.AuthorizedClient(login.SessionToken);
    }

    [Fact]
    public async Task Reach_requires_a_session()
    {
        await ReadyAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(IntelPaths.Reach(Origin, jumps: 2))).StatusCode);
    }

    [Fact]
    public async Task Jumps_and_light_years_give_the_union_with_both_distances_on_every_row()
    {
        var client = await ReadyAsync();

        var reach = await client.GetFromJsonAsync<IntelReach>(IntelPaths.Reach(Origin, jumps: 2, ly: 8));

        Assert.Equal((4, (int?)2, (double?)8), (reach!.Total, reach.Jumps, reach.Ly));
        var rows = reach.Systems.OrderBy(s => s.SystemId).Select(s => (s.SystemId, s.Jumps, s.Ly, s.CynoTarget)).ToList();
        Assert.Equal(
            [(Origin, (int?)0, 0.0, true), (HighSecNeighbour, 1, 5.0, false), (NullSecTwoJumps, 2, 12.0, true), (FarCyno, null, 7.0, true)],
            rows);
        Assert.All(reach.Systems, s => Assert.Null(s.Activity)); // No activity data yet.
    }

    [Fact]
    public async Task Without_a_profile_the_rise_in_npc_kills_comes_first()
    {
        PublishIntel();
        var client = await ReadyAsync();

        var reach = await client.GetFromJsonAsync<IntelReach>(IntelPaths.Reach(Origin, jumps: 2, ly: 8));

        Assert.Equal([FarCyno, NullSecTwoJumps, Origin, HighSecNeighbour], reach!.Systems.Select(s => s.SystemId));
        Assert.Equal((3L, 5L), (reach.ActivityVersion, reach.HuntingVersion));
        var nullSec = reach.Systems[1];
        Assert.Equal((50, (int?)20, 500, (double?)12.5, (double?)0.9, (int?)0), (nullSec.Activity!.NpcKills.LastHour, nullSec.Activity.NpcKills.PreviousHour, nullSec.Activity.NpcKills.Day, nullSec.Activity.NpcKills.Typical, nullSec.Activity.NpcKills.PeakRatio, nullSec.Activity.NpcKills.HoursToPeak));

        // No row: no activity in the last day, with a previous hour of 0 since the others have one.
        var quiet = reach.Systems[3].Activity!.NpcKills;
        Assert.Equal((0, (int?)0, (double?)null), (quiet.LastHour, quiet.PreviousHour, quiet.Typical));
    }

    [Fact]
    public async Task With_a_profile_the_systems_where_it_is_likely_now_come_first()
    {
        PublishIntel();
        var client = await ReadyAsync();

        var reach = await client.GetFromJsonAsync<IntelReach>(IntelPaths.Reach(Origin, jumps: 2, ly: 8, profile: "carriers"));

        // The null-sec system has carriers at this hour; the far target more of them, but at another hour.
        Assert.Equal([NullSecTwoJumps, FarCyno], reach!.Systems.Take(2).Select(s => s.SystemId));
        var likely = Assert.Single(reach.Systems[0].Profiles);
        Assert.Equal(("carriers", 4.0, 2.0, (double?)1.0), (likely.Profile, likely.Score, likely.LikelyNow, likely.PeakNow));
        var later = Assert.Single(reach.Systems[1].Profiles);
        Assert.Equal((0.0, (double?)0.0), (later.LikelyNow, later.PeakNow));
        Assert.Equal(_hour, reach.HourOfDay);
    }

    [Fact]
    public async Task The_limit_caps_the_rows_but_not_the_total()
    {
        var client = await ReadyAsync();

        var reach = await client.GetFromJsonAsync<IntelReach>(IntelPaths.Reach(Origin, jumps: 2, ly: 8, limit: 2));

        Assert.Equal((4, 2), (reach!.Total, reach.Systems.Count));
    }

    [Theory]
    [InlineData(null, null, null, "jumps")]
    [InlineData(51, null, null, "jumps")]
    [InlineData(null, 25.0, null, "ly")]
    [InlineData(2, null, "titans", "profile")]
    public async Task Bad_limits_and_unknown_profiles_are_rejected(int? jumps, double? ly, string? profile, string field)
    {
        PublishIntel();
        var client = await ReadyAsync();

        var response = await client.GetAsync(IntelPaths.Reach(Origin, jumps, ly, profile));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains($"\"{field}\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_origin_off_the_map_is_not_found_and_answers_are_revalidated()
    {
        var client = await ReadyAsync();

        var missing = await client.GetAsync(IntelPaths.Reach(31000005, jumps: 2));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains(ReachEndpoints.NotFoundCode, await missing.Content.ReadAsStringAsync());

        var first = await client.GetAsync(IntelPaths.Reach(Origin, jumps: 2));
        using var again = new HttpRequestMessage(HttpMethod.Get, IntelPaths.Reach(Origin, jumps: 2));
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
    }
}

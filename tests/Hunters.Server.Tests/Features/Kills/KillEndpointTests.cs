using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Contracts.Discovery;
using NewEdenSoft.Hunters.Contracts.Kills;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Server.Features.Hunting;
using NewEdenSoft.Hunters.Server.Features.Kills;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Tests.Features.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Kills;

[Collection(PostgresCollection.Name)]
public sealed class KillEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int Jita = 30000142, Perimeter = 30000144, Thera = 31000005;
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 7, 0, 0, TimeSpan.Zero);

    private readonly HuntersServerFactory _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        await using var db = IntelDb();
        await db.Database.MigrateAsync();
        await db.Kills.ExecuteDeleteAsync();
        await db.HuntingSystemStats.ExecuteDeleteAsync();
        await db.HuntingRegionStats.ExecuteDeleteAsync();
        await db.HuntingProfileStates.ExecuteDeleteAsync();
        await db.Versions.Where(v => v.Dataset == IntelVersion.Hunting).ExecuteDeleteAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private IntelDbContext IntelDb() => new(new DbContextOptionsBuilder<IntelDbContext>()
        .UseNpgsql(postgres.ConnectionString, IntelDbContext.ConfigureNpgsql).Options);

    private static Kill Kill(long id, int system, DateTimeOffset time, int finalBlow = -1, bool npc = false) => new()
    {
        KillmailId = id, Hash = $"hash{id}", Sequence = id, Time = time, SystemId = system, Npc = npc, TotalValue = 1_500_000.5,
        VictimCharacterId = 9000 + id, VictimCorporationId = 98000001, VictimShipTypeId = 587,
        AttackerCount = 2,
        AttackerCharacterIds = [2114733361, 0],
        AttackerCorporationIds = [98000002, 0],
        AttackerAllianceIds = [99000003, 0],
        AttackerShipTypeIds = [22852, 30428],
        AttackerWeaponTypeIds = [40566, 0],
        FinalBlow = finalBlow,
    };

    /// <summary>The kill feed has run: kills in Jita, one in Perimeter, and hunting aggregates (the feature's signal).</summary>
    private async Task SeedAsync(params Kill[] kills)
    {
        await using var db = IntelDb();
        db.Kills.AddRange(kills);
        db.HuntingProfileStates.Add(new HuntingProfileState { Profile = "carriers", Name = "Carriers", Definition = "d" });
        db.Versions.Add(new IntelVersion { Dataset = IntelVersion.Hunting, Version = 1, UpdatedAt = Now });
        await db.SaveChangesAsync();
        await _factory.Services.GetRequiredService<HuntingService>().RefreshAsync(CancellationToken.None);
    }

    private async Task<HttpClient> ReadyAsync()
    {
        var store = _factory.Services.GetRequiredService<MapPackStore>();
        for (var i = 0; store.Current is null && i < 200; i++)
            await Task.Delay(25);
        Assert.NotNull(store.Current);

        var driver = new LoginDriver(_factory);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Hunter"));
        return driver.AuthorizedClient(login.SessionToken);
    }

    [Fact]
    public async Task Kills_require_a_session()
    {
        await SeedAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(KillPaths.System(Jita))).StatusCode);
    }

    [Fact]
    public async Task Kills_are_unavailable_until_the_intel_service_has_read_its_feed()
    {
        var client = await ReadyAsync();

        var response = await client.GetAsync(KillPaths.System(Jita));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(KillEndpoints.UnavailableCode, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(HuntersFeatures.Kills, (await client.GetFromJsonAsync<DiscoveryDocument>(DiscoveryDocument.Path))!.Features);
    }

    [Fact]
    public async Task A_system_has_its_newest_kills_first_with_the_final_blow()
    {
        await SeedAsync(
            Kill(1, Jita, Now.AddHours(-3)),
            Kill(2, Jita, Now.AddMinutes(-5), finalBlow: 0),
            Kill(3, Perimeter, Now.AddMinutes(-1), finalBlow: 0),
            Kill(4, Jita, Now.AddMinutes(-30), finalBlow: 1, npc: true));
        var client = await ReadyAsync();

        Assert.Contains(HuntersFeatures.Kills, (await client.GetFromJsonAsync<DiscoveryDocument>(DiscoveryDocument.Path))!.Features);

        var jita = await client.GetFromJsonAsync<SystemKills>(KillPaths.System(Jita));
        Assert.Equal(Jita, jita!.SystemId);
        Assert.Equal([2L, 4L, 1L], jita.Kills.Select(k => k.KillmailId));

        var newest = jita.Kills[0];
        Assert.Equal(("hash2", Now.AddMinutes(-5), 1_500_000.5, 2), (newest.Hash, newest.Time, newest.TotalValue, newest.AttackerCount));
        Assert.Equal(new KillParty(9002, 98000001, null, 587), newest.Victim);
        Assert.Equal(new KillParty(2114733361, 98000002, 99000003, 22852), newest.FinalBlow);

        // An NPC's final blow: its ship, no pilot. A kill stored before the final blow was kept: unknown.
        Assert.Equal(new KillParty(null, null, null, 30428), jita.Kills[1].FinalBlow);
        Assert.True(jita.Kills[1].Npc);
        Assert.Null(jita.Kills[2].FinalBlow);
    }

    [Fact]
    public async Task The_limit_caps_the_list_and_is_validated()
    {
        await SeedAsync(Kill(1, Jita, Now.AddHours(-2)), Kill(2, Jita, Now.AddHours(-1)));
        var client = await ReadyAsync();

        Assert.Equal([2L], (await client.GetFromJsonAsync<SystemKills>(KillPaths.System(Jita, 1)))!.Kills.Select(k => k.KillmailId));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(KillPaths.System(Jita, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(KillPaths.System(Jita, KillPaths.MaxLimit + 1))).StatusCode);
    }

    [Fact]
    public async Task A_system_off_the_map_is_not_found_and_a_quiet_one_is_empty()
    {
        await SeedAsync(Kill(1, Jita, Now));
        var client = await ReadyAsync();

        var thera = await client.GetAsync(KillPaths.System(Thera));
        Assert.Equal(HttpStatusCode.NotFound, thera.StatusCode);
        Assert.Contains(KillEndpoints.NotFoundCode, await thera.Content.ReadAsStringAsync());
        Assert.Empty((await client.GetFromJsonAsync<SystemKills>(KillPaths.System(Perimeter)))!.Kills);
    }

    [Fact]
    public async Task The_etag_changes_when_a_kill_arrives()
    {
        await SeedAsync(Kill(1, Jita, Now.AddHours(-1)));
        var client = await ReadyAsync();

        var first = await client.GetAsync(KillPaths.System(Jita));
        var etag = first.Headers.ETag!;
        using (var again = new HttpRequestMessage(HttpMethod.Get, KillPaths.System(Jita)))
        {
            again.Headers.IfNoneMatch.Add(etag);
            Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
        }

        await using (var db = IntelDb())
        {
            db.Kills.Add(Kill(2, Jita, Now));
            await db.SaveChangesAsync();
        }

        using var after = new HttpRequestMessage(HttpMethod.Get, KillPaths.System(Jita));
        after.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag.Tag));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(after)).StatusCode);
    }
}

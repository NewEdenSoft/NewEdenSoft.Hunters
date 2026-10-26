using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Contracts.Discovery;
using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Server.Features.Hunting;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Tests.Features.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Hunting;

[Collection(PostgresCollection.Name)]
public sealed class HuntingEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int Jita = 30000142, Perimeter = 30000144, Sobaseki = 30001363, Thera = 31000005;
    private const int TheForge = 10000002, Lonetrek = 10000016;
    private static readonly DateTimeOffset UpdatedAt = new(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);

    private readonly HuntersServerFactory _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        await using var db = IntelDb();
        await db.Database.MigrateAsync();
        await db.HuntingSystemStats.ExecuteDeleteAsync();
        await db.HuntingRegionStats.ExecuteDeleteAsync();
        await db.HuntingRegulars.ExecuteDeleteAsync();
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

    private static double[] Hours(int hour, double weight)
    {
        var hours = new double[24];
        hours[hour] = weight;
        return hours;
    }

    /// <summary>
    /// What the intel service would have written: carriers in Jita (strong) and Sobaseki, a CRAB beacon in
    /// Jita, nothing in Perimeter; and a row of a profile it no longer lists.
    /// </summary>
    private async Task SeedAsync(long version = 1)
    {
        await using var db = IntelDb();
        await db.HuntingSystemStats.ExecuteDeleteAsync();
        await db.HuntingRegionStats.ExecuteDeleteAsync();
        if (!await db.HuntingProfileStates.AnyAsync())
        {
            db.HuntingProfileStates.AddRange(
                new HuntingProfileState { Profile = "carriers", Name = "Carriers", Definition = "d", Description = "Carrier pilots.", Evidence = ["A Carrier dies there.", "A CRAB beacon is killed there."] },
                new HuntingProfileState { Profile = "crab", Name = "CRAB beacons", Definition = "d" },
                new HuntingProfileState { Profile = "ishtars", Name = "Ishtars", Definition = "d" });
        }

        db.HuntingSystemStats.AddRange(
            new HuntingSystemStats { Profile = "carriers", SystemId = Sobaseki, Score = 1.2, Week = 1, Month = 2, Pilots = 1, LastSeen = UpdatedAt.AddDays(-2), Hours = Hours(3, 2) },
            new HuntingSystemStats { Profile = "carriers", SystemId = Jita, Score = 6.12345, Week = 3.75, Month = 7.5, MonthIndicator = 0.75, Pilots = 4, MonthLocal = 4.5, LocalPilots = 2, Reliable = Hours(22, 2.71828), LastSeen = UpdatedAt.AddHours(-1), Hours = Hours(19, 7.5) },
            new HuntingSystemStats { Profile = "crab", SystemId = Jita, Score = 2, Week = 1, Month = 1, LastSeen = UpdatedAt.AddHours(-3), Hours = Hours(17, 1) },
            new HuntingSystemStats { Profile = "titans", SystemId = Jita, Score = 9, Week = 1, Month = 1, LastSeen = UpdatedAt, Hours = Hours(0, 1) });
        // Carriers in Jita: one pilot on an evening session, one only within the extended window.
        await db.HuntingRegulars.ExecuteDeleteAsync();
        db.HuntingRegulars.AddRange(
            new HuntingSystemRegular { Profile = "carriers", SystemId = Jita, PilotId = 2, Days = 2, DaysInWindow = 2, WindowStart = 14, WindowHours = 6, LastSeen = UpdatedAt.AddDays(-2), Strength = 0.9 },
            new HuntingSystemRegular { Profile = "carriers", SystemId = Jita, PilotId = 1, Days = 4, DaysInWindow = 3, WindowStart = 22, WindowHours = 4, LastSeen = UpdatedAt.AddHours(-1), Local = true, Strength = 2.71828 });
        db.HuntingRegionStats.AddRange(
            new HuntingRegionStats { Profile = "carriers", RegionId = TheForge, Score = 6.12345, Week = 3.75, Month = 7.5, MonthIndicator = 0.75, Pilots = 4, LastSeen = UpdatedAt.AddHours(-1), Hours = Hours(19, 7.5) },
            new HuntingRegionStats { Profile = "carriers", RegionId = Lonetrek, Score = 1.2, Week = 1, Month = 2, Pilots = 1, LastSeen = UpdatedAt.AddDays(-2), Hours = Hours(3, 2) });

        var row = await db.Versions.FindAsync(IntelVersion.Hunting);
        if (row is null)
            db.Versions.Add(row = new IntelVersion { Dataset = IntelVersion.Hunting });
        row.Version = version;
        row.UpdatedAt = UpdatedAt;
        await db.SaveChangesAsync();
    }

    private Task<bool> RefreshAsync() => _factory.Services.GetRequiredService<HuntingService>().RefreshAsync(CancellationToken.None);

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
    public async Task Hunting_requires_a_session()
    {
        await SeedAsync();
        await RefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(HuntingPaths.Overview)).StatusCode);
    }

    [Fact]
    public async Task Hunting_is_unavailable_until_the_intel_service_has_aggregates()
    {
        var client = await ReadyAsync();
        await RefreshAsync();

        var response = await client.GetAsync(HuntingPaths.Profiles);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(HuntingEndpoints.UnavailableCode, await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(HuntersFeatures.Hunting, (await client.GetFromJsonAsync<DiscoveryDocument>(DiscoveryDocument.Path))!.Features);

        await SeedAsync();
        Assert.True(await RefreshAsync());

        Assert.Contains(HuntersFeatures.Hunting, (await client.GetFromJsonAsync<DiscoveryDocument>(DiscoveryDocument.Path))!.Features);
        var profiles = (await client.GetFromJsonAsync<List<HuntingProfileInfo>>(HuntingPaths.Profiles))!;
        Assert.Equal(["carriers Carriers", "crab CRAB beacons", "ishtars Ishtars"], profiles.Select(p => $"{p.Id} {p.Name}"));
        // Each with what it is and what counts as seeing it, from the intel service.
        Assert.Equal("Carrier pilots.", profiles[0].Description);
        Assert.Equal(["A Carrier dies there.", "A CRAB beacon is killed there."], profiles[0].Evidence!);
        Assert.Equal("", profiles[1].Description);
        Assert.Empty(profiles[1].Evidence!);
    }

    [Fact]
    public async Task The_overview_lists_each_profiles_systems_by_score()
    {
        await SeedAsync();
        await RefreshAsync();

        var overview = await (await ReadyAsync()).GetFromJsonAsync<HuntingOverview>(HuntingPaths.Overview);

        Assert.Equal((1L, UpdatedAt), (overview!.Version, overview.UpdatedAt));
        Assert.Equal(["carriers", "crab", "ishtars"], overview.Profiles.Select(p => p.Profile)); // Not the unlisted "titans".
        var carriers = overview.Profiles[0];
        Assert.Equal([Jita, Sobaseki], carriers.SystemIds);
        Assert.Equal([6.123, 1.2], carriers.Score);
        Assert.Equal([3.75, 1], carriers.Week);
        Assert.Equal([7.5, 2], carriers.Month);
        Assert.Equal([UpdatedAt.AddHours(-1), UpdatedAt.AddDays(-2)], carriers.LastSeen);
        Assert.Equal(7.5, carriers.Hours[0][19]);
        Assert.Equal(24, carriers.Hours[1].Count);
        // Where someone reliably is at 22:00; Sobaseki has no regulars (no curve stored).
        Assert.Equal(2.718, carriers.Reliable![0][22]);
        Assert.Empty(carriers.Reliable[1]);
        Assert.Empty(overview.Profiles[2].SystemIds);
    }

    [Fact]
    public async Task The_overview_is_compressed_and_revalidated_with_the_version()
    {
        await SeedAsync();
        await RefreshAsync();
        var client = await ReadyAsync();

        using var first = new HttpRequestMessage(HttpMethod.Get, HuntingPaths.Overview);
        first.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        var response = await client.SendAsync(first);
        Assert.Equal("br", Assert.Single(response.Content.Headers.ContentEncoding));
        await using (var body = new BrotliStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress))
            Assert.Equal(1, (await JsonSerializer.DeserializeAsync<HuntingOverview>(body, JsonSerializerOptions.Web))!.Version);

        var etag = response.Headers.ETag!;
        using var again = new HttpRequestMessage(HttpMethod.Get, HuntingPaths.Overview);
        again.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);

        await SeedAsync(version: 2);
        Assert.True(await RefreshAsync());
        using var after = new HttpRequestMessage(HttpMethod.Get, HuntingPaths.Overview);
        after.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(after)).StatusCode);
    }

    [Fact]
    public async Task A_system_lists_the_profiles_seen_there()
    {
        await SeedAsync();
        await RefreshAsync();
        var client = await ReadyAsync();

        var jita = await client.GetFromJsonAsync<HuntingPlace>(HuntingPaths.System(Jita));
        Assert.Equal((Jita, 1L), (jita!.Id, jita.Version));
        Assert.Equal(["carriers", "crab"], jita.Profiles.Select(p => p.Profile));
        var carriers = jita.Profiles[0];
        Assert.Equal(("Carriers", 6.123, 0.75, 4), (carriers.Name, carriers.Score, carriers.MonthIndicator, carriers.Pilots));
        Assert.Equal((4.5, 2), (carriers.MonthLocal, carriers.LocalPilots));
        Assert.Equal(7.5, carriers.Hours[19]);
        // The session regular first.
        Assert.Equal([(1L, 4, 3, 22, 4, true, 2.718), (2L, 2, 2, 14, 6, false, 0.9)],
            carriers.Regulars!.Select(r => (r.PilotId, r.Days, r.DaysInWindow, r.WindowStart, r.WindowHours, r.Local, r.Strength)));
        Assert.Equal(2.718, carriers.Reliable![22]);
        Assert.Empty(jita.Profiles[1].Regulars!);

        Assert.Empty((await client.GetFromJsonAsync<HuntingPlace>(HuntingPaths.System(Perimeter)))!.Profiles);

        var thera = await client.GetAsync(HuntingPaths.System(Thera));
        Assert.Equal(HttpStatusCode.NotFound, thera.StatusCode);
        Assert.Contains(HuntingEndpoints.NotFoundCode, await thera.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_region_lists_the_profiles_seen_in_its_systems()
    {
        await SeedAsync();
        await RefreshAsync();
        var client = await ReadyAsync();

        var forge = await client.GetFromJsonAsync<HuntingPlace>(HuntingPaths.Region(TheForge));
        Assert.Equal(("carriers", 7.5), (Assert.Single(forge!.Profiles).Profile, forge.Profiles[0].Month));
        Assert.Empty(forge.Profiles[0].Regulars!); // Per system only.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(HuntingPaths.Region(Jita))).StatusCode);
    }
}

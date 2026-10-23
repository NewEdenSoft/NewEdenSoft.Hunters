using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Contracts.Activity;
using NewEdenSoft.Hunters.Contracts.Discovery;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Server.Features.Activity;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Tests.Features.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Activity;

[Collection(PostgresCollection.Name)]
public sealed class ActivityEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const int Jita = 30000142, Perimeter = 30000144, Sobaseki = 30001363, Thera = 31000005;
    private const int TheForge = 10000002, Lonetrek = 10000016;

    /// <summary>Like ESI's Last-Modified: the latest hour ended ten minutes ago.</summary>
    private static readonly DateTimeOffset PeriodEnd = new(DateTimeOffset.UtcNow.AddMinutes(-10).Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);

    private static readonly short LastHour = HourOfWeek.Covered(PeriodEnd);

    private readonly HuntersServerFactory _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        // The intel service owns this schema; tests create it the same way.
        await using var db = IntelDb();
        await db.Database.MigrateAsync();
        await db.Versions.ExecuteDeleteAsync();
        await db.SystemActivityCurrent.ExecuteDeleteAsync();
        await db.RegionActivityCurrent.ExecuteDeleteAsync();
        await db.SystemActivityWeek.ExecuteDeleteAsync();
        await db.RegionActivityWeek.ExecuteDeleteAsync();
        await db.ActivityWeekSamples.ExecuteDeleteAsync();
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private IntelDbContext IntelDb() => new(new DbContextOptionsBuilder<IntelDbContext>()
        .UseNpgsql(postgres.ConnectionString, IntelDbContext.ConfigureNpgsql).Options);

    private static ActivityMetric Metric(int last, int? previous = null, int day = 0, double? typical = null, double? peak = null, short? toPeak = null, short? peakHour = null) =>
        new() { LastHour = last, PreviousHour = previous, Day = day, Typical = typical, PeakRatio = peak, HoursToPeak = toPeak, PeakHourOfWeek = peakHour };

    /// <summary>
    /// What the intel service would have written: Jita busy with a peak, Sobaseki quiet, Thera (not on
    /// the map pack) too; two weeks of samples at the latest hour, one the hour before, none otherwise.
    /// </summary>
    private async Task SeedAsync(long version = 1)
    {
        await using var db = IntelDb();
        SystemActivityCurrent System(int id, ActivityMetric npc, ActivityMetric jumps) => new()
        {
            SystemId = id, KillsPeriodEnd = PeriodEnd, JumpsPeriodEnd = PeriodEnd, KillsDayHours = 24, JumpsDayHours = 23,
            NpcKills = npc, ShipKills = Metric(2, 1, 9, 0.5), PodKills = Metric(0, 0, 1, 0), ShipJumps = jumps,
        };

        await db.SystemActivityCurrent.ExecuteDeleteAsync();
        await db.RegionActivityCurrent.ExecuteDeleteAsync();
        db.SystemActivityCurrent.AddRange(
            System(Jita, Metric(120, 80, 2000, 95.5, 0.8567, 0, 139), Metric(1500, 1400, 30000, 1450)),
            System(Sobaseki, Metric(3, 0, 40, 2), Metric(10, 12, 300, 11)),
            System(Thera, Metric(7, 1, 50, 5), Metric(0, 0, 0, 0)));
        db.RegionActivityCurrent.Add(new RegionActivityCurrent
        {
            RegionId = TheForge, KillsPeriodEnd = PeriodEnd, JumpsPeriodEnd = PeriodEnd, KillsDayHours = 24, JumpsDayHours = 23,
            NpcKills = Metric(120, 80, 2000, 95), ShipKills = Metric(2, 1, 9, 0.5), PodKills = Metric(0, 0, 1, 0), ShipJumps = Metric(1500, 1400, 30000, 1450),
        });

        if (!await db.ActivityWeekSamples.AnyAsync())
        {
            db.ActivityWeekSamples.AddRange(
                new ActivityWeekSamples { HourOfWeek = LastHour, KillHours = 2, JumpHours = 2 },
                new ActivityWeekSamples { HourOfWeek = Previous(LastHour), KillHours = 1, JumpHours = 0 });
            db.SystemActivityWeek.AddRange(
                new SystemActivityWeek { SystemId = Jita, HourOfWeek = LastHour, NpcKills = 191, ShipKills = 1, ShipJumps = 2900 },
                new SystemActivityWeek { SystemId = Jita, HourOfWeek = Previous(LastHour), NpcKills = 50 });
            db.RegionActivityWeek.Add(new RegionActivityWeek { RegionId = TheForge, HourOfWeek = LastHour, NpcKills = 200 });
        }

        var row = await db.Versions.FindAsync(IntelVersion.Activity);
        if (row is null)
            db.Versions.Add(row = new IntelVersion { Dataset = IntelVersion.Activity });
        row.Version = version;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
    }

    private static short Previous(short hour) => (short)((hour + HourOfWeek.Count - 1) % HourOfWeek.Count);

    private Task<bool> RefreshAsync() => _factory.Services.GetRequiredService<ActivityService>().RefreshAsync(CancellationToken.None);

    private async Task<HttpClient> ReadyAsync()
    {
        var store = _factory.Services.GetRequiredService<MapPackStore>();
        for (var i = 0; store.Current is null && i < 200; i++)
            await Task.Delay(25);
        Assert.NotNull(store.Current);

        var driver = new LoginDriver(_factory);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Analyst"));
        return driver.AuthorizedClient(login.SessionToken);
    }

    [Fact]
    public async Task Activity_requires_a_session()
    {
        var response = await _factory.CreateClient().GetAsync(ActivityPaths.Current);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Activity_is_unavailable_until_the_intel_service_has_an_hour()
    {
        var client = await ReadyAsync();
        await RefreshAsync();

        var response = await client.GetAsync(ActivityPaths.Current);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains(ActivityEndpoints.UnavailableCode, await response.Content.ReadAsStringAsync());
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Empty((await client.GetFromJsonAsync<DiscoveryDocument>(DiscoveryDocument.Path))!.Features);

        await SeedAsync();
        Assert.True(await RefreshAsync());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(ActivityPaths.Current)).StatusCode);
        Assert.Equal([HuntersFeatures.Activity], (await client.GetFromJsonAsync<DiscoveryDocument>(DiscoveryDocument.Path))!.Features);
    }

    [Fact]
    public async Task A_missing_intel_schema_just_means_no_activity()
    {
        await using (var db = IntelDb())
            await db.Database.ExecuteSqlRawAsync("DROP SCHEMA intel CASCADE");

        Assert.False(await RefreshAsync());
        Assert.Null(_factory.Services.GetRequiredService<ActivityStore>().Current);
    }

    [Fact]
    public async Task Current_lines_up_with_the_map_pack()
    {
        await SeedAsync();
        await RefreshAsync();

        var current = await (await ReadyAsync()).GetFromJsonAsync<ActivityCurrent>(ActivityPaths.Current);

        // Map pack order: Jita, Perimeter, Sobaseki (by id); The Forge, Lonetrek. Thera isn't on it.
        Assert.Equal((1L, FakeSdeMap.Build, PeriodEnd, 24, 23), (current!.Version, current.SdeBuild, current.KillsPeriodEnd, current.KillsDayHours, current.JumpsDayHours));
        var npc = current.Systems.NpcKills;
        Assert.Equal([120, 0, 3], npc.LastHour);
        Assert.Equal([80, 0, 0], npc.PreviousHour!);
        Assert.Equal([2000, 0, 40], npc.Day);
        Assert.Equal([95.5, 0, 2], npc.Typical!);
        Assert.Equal([0.857, null, null], npc.PeakRatio);
        Assert.Equal([0, null, null], npc.HoursToPeak);
        Assert.Equal([139, null, null], npc.PeakHourOfWeek);
        Assert.Equal([1500, 0, 10], current.Systems.ShipJumps.LastHour);

        Assert.Equal([120, 0], current.Regions.NpcKills.LastHour);
        Assert.Equal([1500, 0], current.Regions.ShipJumps.LastHour);
    }

    [Fact]
    public async Task Current_is_compressed_and_revalidated_with_its_etag()
    {
        await SeedAsync();
        await RefreshAsync();
        var client = await ReadyAsync();

        using var first = new HttpRequestMessage(HttpMethod.Get, ActivityPaths.Current);
        first.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        var response = await client.SendAsync(first);

        Assert.Equal("br", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.InRange(response.Headers.CacheControl!.MaxAge!.Value, TimeSpan.FromMinutes(50), TimeSpan.FromMinutes(53));
        var etag = response.Headers.ETag!;
        await using (var body = new BrotliStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress))
            Assert.Equal(1, (await JsonSerializer.DeserializeAsync<ActivityCurrent>(body, JsonSerializerOptions.Web))!.Version);

        using var again = new HttpRequestMessage(HttpMethod.Get, ActivityPaths.Current);
        again.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);

        // A new hour: a new version and a new ETag.
        await SeedAsync(version: 2);
        Assert.True(await RefreshAsync());
        Assert.False(await RefreshAsync());
        using var after = new HttpRequestMessage(HttpMethod.Get, ActivityPaths.Current);
        after.Headers.IfNoneMatch.Add(etag);
        var changed = await client.SendAsync(after);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.NotEqual(etag, changed.Headers.ETag);
    }

    [Fact]
    public async Task A_system_has_its_week_as_averages_per_hour()
    {
        await SeedAsync();
        await RefreshAsync();

        var jita = await (await ReadyAsync()).GetFromJsonAsync<ActivityDetails>(ActivityPaths.System(Jita));

        Assert.Equal((Jita, 1L, (int)HourOfWeek.Following(PeriodEnd)), (jita!.Id, jita.Version, jita.NowHourOfWeek));
        Assert.Equal((120, (int?)80, 2000, (double?)0.857, (int?)0, (int?)139),
            (jita.NpcKills.LastHour, jita.NpcKills.PreviousHour, jita.NpcKills.Day, jita.NpcKills.PeakRatio, jita.NpcKills.HoursToPeak, jita.NpcKills.PeakHourOfWeek));

        // Two samples at the latest hour, one the hour before, none elsewhere.
        Assert.Equal(HourOfWeek.Count, jita.NpcKills.Week.Count);
        Assert.Equal(95.5, jita.NpcKills.Week[LastHour]);
        Assert.Equal(95.5, jita.NpcKills.Typical);
        Assert.Equal(50, jita.NpcKills.Week[Previous(LastHour)]);
        Assert.Equal(0, jita.PodKills.Week[Previous(LastHour)]);
        Assert.Equal(1450, jita.ShipJumps.Week[LastHour]);
        Assert.Null(jita.ShipJumps.Week[Previous(LastHour)]);
        Assert.Equal(HourOfWeek.Count - 2, jita.NpcKills.Week.Count(w => w is null));
    }

    [Fact]
    public async Task A_quiet_system_has_zeros_and_unknown_ids_are_not_found()
    {
        await SeedAsync();
        await RefreshAsync();
        var client = await ReadyAsync();

        var perimeter = await client.GetFromJsonAsync<ActivityDetails>(ActivityPaths.System(Perimeter));
        Assert.Equal((0, (int?)0, 0, (double?)0, (double?)null), (perimeter!.NpcKills.LastHour, perimeter.NpcKills.PreviousHour, perimeter.NpcKills.Day, perimeter.NpcKills.Typical, perimeter.NpcKills.PeakRatio));
        Assert.Equal(0, perimeter.NpcKills.Week[LastHour]);

        var thera = await client.GetAsync(ActivityPaths.System(Thera));
        Assert.Equal(HttpStatusCode.NotFound, thera.StatusCode);
        Assert.Contains(ActivityEndpoints.NotFoundCode, await thera.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(ActivityPaths.Region(Jita))).StatusCode);
    }

    [Fact]
    public async Task A_region_has_its_week_too()
    {
        await SeedAsync();
        await RefreshAsync();
        var client = await ReadyAsync();

        var forge = await client.GetFromJsonAsync<ActivityDetails>(ActivityPaths.Region(TheForge));
        Assert.Equal((120, 100.0), (forge!.NpcKills.LastHour, forge.NpcKills.Week[LastHour]));

        var lonetrek = await client.GetFromJsonAsync<ActivityDetails>(ActivityPaths.Region(Lonetrek));
        Assert.Equal(0, lonetrek!.NpcKills.LastHour);
    }

    [Fact]
    public async Task Details_are_revalidated_with_the_version()
    {
        await SeedAsync();
        await RefreshAsync();
        var client = await ReadyAsync();

        var response = await client.GetAsync(ActivityPaths.System(Jita));
        using var again = new HttpRequestMessage(HttpMethod.Get, ActivityPaths.System(Jita));
        again.Headers.IfNoneMatch.Add(response.Headers.ETag!);

        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
    }
}

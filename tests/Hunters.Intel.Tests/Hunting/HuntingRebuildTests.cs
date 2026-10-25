using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Hunting;
using NewEdenSoft.Hunters.Intel.Sde;
using NewEdenSoft.Hunters.Intel.Sovereignty;

namespace NewEdenSoft.Hunters.Intel.Tests.Hunting;

/// <summary>An admin's rebuild request, carried out by the profile service.</summary>
[Collection(IntelDatabaseCollection.Name)]
public sealed class HuntingRebuildTests(IntelDatabase database) : IAsyncLifetime
{
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _services = database.Services(new FakeSystemActivity());
        await using var db = Db();
        await db.Kills.ExecuteDeleteAsync();
        await db.Sightings.ExecuteDeleteAsync();
        await db.HuntingProfileStates.ExecuteDeleteAsync();
        await db.HuntingRebuildRequests.ExecuteDeleteAsync();

        // A carrier and a CRAB beacon dying, and the profiles built from them at startup.
        var carrier = SightingMatcherTests.Kill(FakeSde.Thanatos, victim: 90000001);
        carrier.KillmailId = 1;
        var beacon = SightingMatcherTests.Kill(FakeSde.CrabBeacon);
        beacon.KillmailId = 2;
        db.Kills.AddRange(carrier, beacon);
        await db.SaveChangesAsync();

        await _services.GetRequiredService<SdeCatalogService>().LoadAsync(CancellationToken.None);
        _services.GetRequiredService<SovereigntyStore>().Publish(SovereigntyMap.Empty);
        Assert.Equal(TimeSpan.FromSeconds(30), await Service.StepAsync(CancellationToken.None));
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private IntelDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<IntelDbContext>();

    private HuntingProfileService Service => _services.GetRequiredService<HuntingProfileService>();

    /// <summary>Loses every sighting, as if they had drifted from the kills; only a rebuild brings them back.</summary>
    private async Task LoseSightingsAsync()
    {
        await using var db = Db();
        await db.Sightings.ExecuteDeleteAsync();
    }

    private async Task<string[]> ProfilesSeenAsync()
    {
        await using var db = Db();
        return [.. (await db.Sightings.Select(s => s.Profile).Distinct().ToListAsync()).Order()];
    }

    private async Task<long> RequestAsync(params string[] profiles)
    {
        await using var db = Db();
        var request = new HuntingRebuildRequest { Profiles = profiles, RequestedBy = Guid.NewGuid(), RequestedAt = DateTimeOffset.UtcNow };
        db.HuntingRebuildRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task Nothing_is_rebuilt_without_a_request()
    {
        await LoseSightingsAsync();

        await Service.StepAsync(CancellationToken.None);

        Assert.Empty(await ProfilesSeenAsync());
    }

    [Fact]
    public async Task A_request_rebuilds_the_profiles_it_names_and_is_marked_done()
    {
        await LoseSightingsAsync();
        var id = await RequestAsync("crab");

        await Service.StepAsync(CancellationToken.None);

        Assert.Equal(["crab"], await ProfilesSeenAsync());
        await using var db = Db();
        var request = await db.HuntingRebuildRequests.SingleAsync(r => r.Id == id);
        Assert.NotNull(request.StartedAt);
        Assert.NotNull(request.CompletedAt);
        Assert.Null(request.Error);
        // The definition is back, so the next step leaves the profile alone.
        Assert.All(await db.HuntingProfileStates.ToListAsync(), s => Assert.NotEqual("", s.Definition));
    }

    [Fact]
    public async Task A_request_naming_no_profiles_rebuilds_them_all()
    {
        await LoseSightingsAsync();
        await RequestAsync();

        await Service.StepAsync(CancellationToken.None);

        Assert.Equal(["carriers", "crab", "supers"], await ProfilesSeenAsync());
        await using var db = Db();
        Assert.Empty(await db.HuntingRebuildRequests.Where(r => r.CompletedAt == null).ToListAsync());
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Hunting;
using NewEdenSoft.Hunters.Intel.Kills;
using NewEdenSoft.Hunters.Intel.Sde;
using NewEdenSoft.Hunters.Intel.Tests.Hunting;

namespace NewEdenSoft.Hunters.Intel.Tests.Kills;

[Collection(IntelDatabaseCollection.Name)]
public sealed class KillAffiliationsTests(IntelDatabase database) : IAsyncLifetime
{
    private const long Bomber = 90000002, Gunner = 90000003, Unknown = 90000004;
    private const long Corp = 98000001;

    private readonly FakeAffiliations _affiliations = new();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _services = database.Services(new FakeSystemActivity(), affiliations: _affiliations);
        await using var db = Db();
        await db.Kills.ExecuteDeleteAsync();
        await db.Sightings.ExecuteDeleteAsync();
        await db.HuntingProfileStates.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private IntelDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<IntelDbContext>();

    /// <summary>A kill stored before attackers' corporations were kept: a smartbomber, a gunner, an NPC and a pilot ESI doesn't know.</summary>
    private static Kill Stored(long id)
    {
        var kill = SightingMatcherTests.Kill(FakeSde.Rifter, victim: 90000001,
            attackers: [(Bomber, FakeSde.Rifter, FakeSde.Smartbomb), (Gunner, FakeSde.Rifter, 2873), (0, 0, 0), (Unknown, FakeSde.Rifter, 2873)]);
        kill.KillmailId = id;
        kill.Hash = $"h{id}";
        kill.AttackerCorporationIds = [];
        kill.AttackerAllianceIds = [];
        return kill;
    }

    [Fact]
    public async Task Stored_kills_get_their_attackers_corporations_once()
    {
        _affiliations.Known[Bomber] = new Affiliation(Corp, 99000001);
        _affiliations.Known[Gunner] = new Affiliation(98000009, 0);
        await using (var db = Db())
        {
            db.Kills.AddRange(Stored(1), Stored(2));
            await db.SaveChangesAsync();
        }

        await using (var db = Db())
        {
            Assert.Equal(2, await KillAffiliations.FillAsync(db, _affiliations, CancellationToken.None));
            Assert.Equal(0, await KillAffiliations.FillAsync(db, _affiliations, CancellationToken.None));
        }

        // One call for the distinct pilots of both kills; NPCs aren't asked about.
        Assert.Equal([Bomber, Gunner, Unknown], Assert.Single(_affiliations.Calls).Order());
        await using var check = Db();
        var kill = await check.Kills.SingleAsync(k => k.KillmailId == 1);
        Assert.Equal([Corp, 98000009, 0, 0], kill.AttackerCorporationIds);
        Assert.Equal([99000001, 0, 0, 0], kill.AttackerAllianceIds);
    }

    [Fact]
    public async Task Rebuilding_the_smartbombers_fills_in_the_corporations_first()
    {
        _affiliations.Known[Bomber] = new Affiliation(Corp, 0);
        await using (var db = Db())
        {
            db.Kills.Add(Stored(1));
            await db.SaveChangesAsync();
        }

        await _services.GetRequiredService<SdeCatalogService>().LoadAsync(CancellationToken.None);
        var catalog = _services.GetRequiredService<SdeCatalogStore>().Current!;
        await _services.GetRequiredService<HuntingProfileService>().SyncAsync(catalog, CancellationToken.None);

        await using var check = Db();
        var sighting = Assert.Single(await check.Sightings.Where(s => s.Profile == "smartbombers").ToListAsync());
        Assert.Equal((Bomber, SightingRole.Attacker), (sighting.PilotId, sighting.Role));
    }
}

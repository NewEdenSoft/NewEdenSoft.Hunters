using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Sovereignty;

namespace NewEdenSoft.Hunters.Intel.Tests.Sovereignty;

[Collection(IntelDatabaseCollection.Name)]
public sealed class SovereigntyServiceTests(IntelDatabase database) : IAsyncLifetime
{
    private const long Alliance = 99000001, Corp = 98000001;
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeSovereignty _esi = new();
    private readonly FakeTimeProvider _time = new(Now);
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _services = database.Services(new FakeSystemActivity(), time: _time, sovereignty: _esi);
        await using var db = Db();
        await db.SystemSovereignty.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private IntelDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<IntelDbContext>();

    private SovereigntyService Service => _services.GetRequiredService<SovereigntyService>();

    private SovereigntyStore Store => _services.GetRequiredService<SovereigntyStore>();

    [Fact]
    public async Task Esi_list_replaces_the_stored_one_and_is_published_until_its_expiry()
    {
        await using (var db = Db())
        {
            db.SystemSovereignty.Add(new SystemSovereignty { SystemId = FakeSde.Sobaseki, AllianceId = 99000009, CorporationId = 98000009 });
            await db.SaveChangesAsync();
        }

        _esi.Claims.Add(new SovereignClaim(FakeSde.Jita, Alliance, Corp, Now.AddDays(-3)));
        _esi.Expires = Now.AddMinutes(40);

        Assert.Equal(TimeSpan.FromMinutes(40) + TimeSpan.FromSeconds(30), await Service.RefreshAsync(CancellationToken.None));

        await using var check = Db();
        var held = Assert.Single(await check.SystemSovereignty.ToListAsync());
        Assert.Equal((FakeSde.Jita, Alliance, Corp, Now.AddDays(-3), Now), (held.SystemId, held.AllianceId, held.CorporationId, held.ClaimedSince, held.UpdatedAt));
        Assert.True(Store.Current.IsLocal(FakeSde.Jita, Alliance, Now));
        Assert.False(Store.Current.IsLocal(FakeSde.Sobaseki, 99000009, Now));
    }

    [Fact]
    public async Task The_stored_map_is_ready_at_startup_without_esi()
    {
        await using (var db = Db())
        {
            db.SystemSovereignty.Add(new SystemSovereignty { SystemId = FakeSde.Jita, AllianceId = Alliance, CorporationId = Corp });
            await db.SaveChangesAsync();
        }

        Assert.False(Store.WhenReadyAsync(CancellationToken.None).IsCompleted);
        await Service.LoadStoredAsync(CancellationToken.None);

        Assert.True((await Store.WhenReadyAsync(CancellationToken.None)).IsLocal(FakeSde.Jita, Alliance, Now));
    }

    [Fact]
    public async Task A_failure_backs_off_and_keeps_the_last_map()
    {
        _esi.Claims.Add(new SovereignClaim(FakeSde.Jita, Alliance, Corp, null));
        await Service.RefreshAsync(CancellationToken.None);

        _esi.Failure = new HttpRequestException("ESI answered 503");
        Assert.Equal(TimeSpan.FromMinutes(1), await Service.RefreshAsync(CancellationToken.None));
        Assert.True(Store.Current.IsLocal(FakeSde.Jita, Alliance, Now));

        // No expiry from ESI: an hour.
        _esi.Failure = null;
        Assert.Equal(TimeSpan.FromHours(1), await Service.RefreshAsync(CancellationToken.None));
    }
}

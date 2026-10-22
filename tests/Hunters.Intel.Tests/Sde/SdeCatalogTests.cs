using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Intel.Sde;
using NewEdenSoft.Hunters.Sde;

namespace NewEdenSoft.Hunters.Intel.Tests.Sde;

public sealed class SdeCatalogTests
{
    private readonly FakeSde _sde = new();

    private async Task<SdeCatalog> CatalogAsync() =>
        SdeCatalog.Build(await _sde.LoadAsync(CancellationToken.None), await ((ISdeTypeSource)_sde).LoadAsync(CancellationToken.None));

    [Fact]
    public async Task Systems_have_their_region_and_a_3d_position_in_light_years()
    {
        var catalog = await CatalogAsync();
        var jita = catalog.Systems[FakeSde.Jita];
        var perimeter = catalog.Systems[FakeSde.Perimeter];

        Assert.Equal(FakeSde.TheForge, perimeter.RegionId);
        Assert.Equal("Lonetrek", catalog.Regions[catalog.Systems[FakeSde.Sobaseki].RegionId]);
        Assert.Equal((3.0, 4.0, 0.0), (perimeter.X, perimeter.Y, perimeter.Z));
        Assert.Equal(5.0, jita.DistanceLy(perimeter), 9);
    }

    [Fact]
    public async Task Types_know_their_group_and_category()
    {
        var thanatos = (await CatalogAsync()).Types[FakeSde.Thanatos];

        Assert.Equal(FakeSde.CarrierGroup, thanatos.GroupId);
        Assert.Equal(FakeSde.ShipCategory, thanatos.CategoryId);
    }

    [Fact]
    public async Task Types_are_found_by_name_preferring_published_ones()
    {
        var catalog = await CatalogAsync();

        Assert.Equal(FakeSde.CrabBeacon, catalog.FindType("concord rogue analysis beacon")?.Id);
        Assert.Null(catalog.FindType("Rogue Analysis Beacon"));
    }

    [Fact]
    public async Task The_service_publishes_once_per_build_and_keeps_the_catalogue_through_failures()
    {
        await using var services = new IntelDatabase().Services(new FakeSystemActivity(), sde: _sde);
        var worker = services.GetRequiredService<SdeCatalogService>();
        var store = services.GetRequiredService<SdeCatalogStore>();

        Assert.Equal(TimeSpan.FromHours(6), await worker.LoadAsync(CancellationToken.None));
        var first = await store.WhenReadyAsync(CancellationToken.None);

        await worker.LoadAsync(CancellationToken.None);
        Assert.Same(first, store.Current);

        _sde.Failure = new HttpRequestException("CCP is down");
        Assert.Equal(TimeSpan.FromMinutes(1), await worker.LoadAsync(CancellationToken.None));
        Assert.Equal(TimeSpan.FromMinutes(2), await worker.LoadAsync(CancellationToken.None));
        Assert.Same(first, store.Current);

        _sde.Failure = null;
        _sde.Build++;
        await worker.LoadAsync(CancellationToken.None);
        Assert.NotSame(first, store.Current);
        Assert.Equal(_sde.Build, store.Current!.MapBuild);
    }
}

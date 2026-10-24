using NewEdenSoft.Hunters.Contracts.Map;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Map;

public sealed class MapPackBuilderTests
{
    private readonly MapPack _pack = MapPackBuilder.Build(new FakeSdeMap().Data);

    [Fact]
    public void Keeps_known_space_systems_reachable_by_stargate()
    {
        Assert.Equal(["Jita", "Perimeter", "Sobaseki"], _pack.Systems.Select(s => s.Name));
        Assert.Equal(FakeSdeMap.Build, _pack.SdeBuild);
    }

    [Fact]
    public void Lists_each_gate_once_ordered_by_id()
    {
        Assert.Equal([new MapGate(30000142, 30000144), new MapGate(30000142, 30001363)], _pack.Gates);
    }

    [Fact]
    public void Lists_only_regions_with_systems()
    {
        Assert.Equal(["The Forge", "Lonetrek"], _pack.Regions.Select(r => r.Name));
    }

    [Fact]
    public void Positions_are_light_years_with_y_growing_south()
    {
        var jita = _pack.Systems.Single(s => s.Name == "Jita");

        Assert.Equal(new MapPoint(-13, -12), jita.TopDown);
        Assert.Equal(new MapPoint(-4, -18), jita.Flat);
        Assert.Equal(0.9459, jita.Security);
    }

    [Fact]
    public void Heights_are_the_third_axis_in_light_years()
    {
        var sde = new FakeSdeMap().Data;
        var raised = sde with
        {
            Systems = [.. sde.Systems.Select(s => s.Name == "Perimeter" ? s with { Y = 2.5 * FakeSdeMap.Ly } : s)],
        };

        var pack = MapPackBuilder.Build(raised);

        Assert.Equal(2.5, pack.Systems.Single(s => s.Name == "Perimeter").Height);
        Assert.Equal(0, pack.Systems.Single(s => s.Name == "Jita").Height);
    }

    [Fact]
    public void Flat_layout_falls_back_to_top_down_when_the_sde_has_none()
    {
        var perimeter = _pack.Systems.Single(s => s.Name == "Perimeter");

        Assert.Equal(perimeter.TopDown, perimeter.Flat);
    }
}

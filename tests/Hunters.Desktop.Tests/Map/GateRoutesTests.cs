using NewEdenSoft.Hunters.Contracts.Map;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class GateRoutesTests
{
    /// <summary>A chain 1 - 2 - 3 - 4 - 5 with a shortcut 1 - 4, and 6 with no gates.</summary>
    private static readonly MapModel Model = MapModel.Build(new MapPack(
        1,
        [new MapRegion(1, "Region")],
        [.. Enumerable.Range(1, 6).Select(id => new MapSystem(id, $"S{id}", 1, 1, -0.5, new MapPoint(0, 0), new MapPoint(0, 0)))],
        [.. Enumerable.Range(1, 4).Select(id => new MapGate(id, id + 1)), new MapGate(1, 4)]));

    [Fact]
    public void Takes_the_fewest_gate_jumps_both_ends_included()
    {
        Assert.Equal([0, 3, 4], GateRoutes.Route(Model, 0, 4)!);
        Assert.Equal([4, 3, 2, 1], GateRoutes.Route(Model, 4, 1)!);
        Assert.Equal([2], GateRoutes.Route(Model, 2, 2)!);
    }

    [Fact]
    public void Is_null_where_no_gates_lead()
    {
        Assert.Null(GateRoutes.Route(Model, 0, 5));
    }
}

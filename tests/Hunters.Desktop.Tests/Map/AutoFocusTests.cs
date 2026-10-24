using NewEdenSoft.Hunters.Contracts.Map;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class AutoFocusTests
{
    private const int Wormhole = 31000005;

    /// <summary>A chain of systems 1 - 2 - 3 - 4 - 5 - 6, and 7 with no gates.</summary>
    private static readonly MapModel Model = MapModel.Build(new MapPack(
        1,
        [new MapRegion(1, "Region")],
        [.. Enumerable.Range(1, 7).Select(id => new MapSystem(id, $"S{id}", 1, 1, -0.5, new MapPoint(0, 0), new MapPoint(0, 0)))],
        [.. Enumerable.Range(1, 5).Select(id => new MapGate(id, id + 1))]));

    private static readonly MapBehaviour Behaviour = new() { MinJumps = 3 };

    [Fact]
    public void Counts_gate_jumps_up_to_the_limit()
    {
        Assert.True(AutoFocus.JumpsWithin(Model, 0, 3, 3));
        Assert.False(AutoFocus.JumpsWithin(Model, 0, 3, 2));
        // No gates lead to S7.
        Assert.False(AutoFocus.JumpsWithin(Model, 0, 6, 10));
    }

    [Fact]
    public void Focuses_on_coming_from_unknown_space_into_known_space()
    {
        Assert.Equal(AutoFocusReason.KnownSpace, AutoFocus.Reason(Model, Wormhole, 2, Behaviour));
        Assert.Null(AutoFocus.Reason(Model, Wormhole, 2, Behaviour with { EnterKnownSpace = false }));
        // Into unknown space: nothing on the map to focus.
        Assert.Null(AutoFocus.Reason(Model, 2, Wormhole, Behaviour));
    }

    [Fact]
    public void Focuses_after_a_move_of_at_least_the_minimum_jumps_or_with_no_gate_route()
    {
        Assert.Null(AutoFocus.Reason(Model, 1, 2, Behaviour));
        Assert.Null(AutoFocus.Reason(Model, 1, 3, Behaviour));
        Assert.Equal(AutoFocusReason.LongMove, AutoFocus.Reason(Model, 1, 4, Behaviour));
        Assert.Equal(AutoFocusReason.LongMove, AutoFocus.Reason(Model, 1, 7, Behaviour));
        Assert.Null(AutoFocus.Reason(Model, 1, 4, Behaviour with { LongMove = false }));
    }

    [Fact]
    public void Never_focuses_a_first_known_location()
    {
        Assert.Null(AutoFocus.Reason(Model, null, 4, Behaviour));
    }
}

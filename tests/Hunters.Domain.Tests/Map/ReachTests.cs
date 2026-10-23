using NewEdenSoft.Hunters.Domain.Map;

namespace NewEdenSoft.Hunters.Domain.Tests.Map;

public sealed class ReachTests
{
    private const int Region = 10000001;
    private const int Origin = 30000001, HighSecNeighbour = 30000002, NullSecTwoJumps = 30000003, FarCyno = 30000004,
        PochvenSystem = 30000005, HalfHighSec = 30000007, JustLowSec = 30000008;

    /// <summary>
    /// A chain of gates Origin – HighSecNeighbour (5 ly, high-sec) – NullSecTwoJumps (12 ly), and systems
    /// without gates around the origin: a null-sec cyno target 7 ly away, Pochven 3 ly, Zarzakh 2 ly, a
    /// 0.45 system (shown 0.5: high-sec) 4 ly and a 0.44 system (shown 0.4) 5 ly.
    /// </summary>
    private static readonly MapGeometry Map = MapGeometry.Build(
        [
            new(Origin, Region, 0.3, 0, 0, 0),
            new(HighSecNeighbour, Region, 0.9, 5, 0, 0),
            new(NullSecTwoJumps, Region, -0.2, 12, 0, 0),
            new(FarCyno, Region, -0.5, 0, 7, 0),
            new(PochvenSystem, CynoTargets.Pochven, -1, 0, 3, 0),
            new(CynoTargets.Zarzakh, 10001000, -1, 0, 0, 2),
            new(HalfHighSec, Region, 0.45, 0, 0, 4),
            new(JustLowSec, Region, 0.44, 0, 0, 5),
        ],
        [(Origin, HighSecNeighbour), (HighSecNeighbour, Origin), (HighSecNeighbour, NullSecTwoJumps), (NullSecTwoJumps, 31000005)]);

    private static List<(int Id, int? Jumps, double Ly, bool Cyno)> Find(int? jumps, double? ly) =>
        [.. Reach.Find(Map, Map.IndexOf[Origin], jumps, ly).Select(r => (Map.Systems[r.Index].Id, r.Jumps, Math.Round(r.Ly, 3), r.CynoTarget))];

    [Fact]
    public void Gate_reach_takes_every_system_within_the_jumps_whatever_its_security_or_distance()
    {
        Assert.Equal(
            [(Origin, 0, 0, true), (HighSecNeighbour, 1, 5, false), (NullSecTwoJumps, 2, 12, true)],
            Find(jumps: 2, ly: null));
        Assert.Equal([Origin, HighSecNeighbour], Find(jumps: 1, ly: null).Select(r => r.Id));
    }

    [Fact]
    public void Jump_drive_reach_takes_cyno_targets_within_the_light_years_however_far_by_gate()
    {
        // Not the high-sec neighbour (5 ly), Pochven (3 ly), Zarzakh (2 ly) or the 0.45 system (4 ly);
        // not the null-sec system 12 ly away. The far cyno target has no gates at all.
        Assert.Equal(
            [(Origin, 0, 0, true), (JustLowSec, null, 5, true), (FarCyno, null, 7, true)],
            Find(jumps: null, ly: 8));
    }

    [Fact]
    public void Both_limits_give_the_union_and_every_row_carries_both_distances()
    {
        Assert.Equal(
            [(Origin, 0, 0, true), (HighSecNeighbour, 1, 5, false), (NullSecTwoJumps, 2, 12, true), (JustLowSec, null, 5, true), (FarCyno, null, 7, true)],
            Find(jumps: 2, ly: 8));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, null)]
    [InlineData(51, null)]
    [InlineData(null, 0.05)]
    [InlineData(null, 20.5)]
    public void Limits_are_checked(int? jumps, double? ly)
    {
        Assert.ThrowsAny<ArgumentException>(() => Reach.Find(Map, 0, jumps, ly));
    }

    [Fact]
    public void Gates_are_undirected_and_gates_to_unknown_systems_are_ignored()
    {
        var jumps = Map.JumpsFrom(Map.IndexOf[NullSecTwoJumps]);

        Assert.Equal(2, jumps[Map.IndexOf[Origin]]);
        Assert.Equal(-1, jumps[Map.IndexOf[FarCyno]]);
    }

    [Theory]
    [InlineData(0.945913, 0.9, true)]
    [InlineData(0.45, 0.5, true)]
    [InlineData(0.449, 0.4, false)]
    [InlineData(0.04, 0.1, false)]
    [InlineData(-0.04, 0.0, false)]
    [InlineData(-0.06, -0.1, false)]
    public void Security_is_shown_like_in_game(double security, double shown, bool highSec)
    {
        Assert.Equal(shown, SecurityStatus.Display(security), 9);
        Assert.Equal(highSec, SecurityStatus.IsHighSec(security));
    }

    [Fact]
    public void Wormhole_space_is_never_a_cyno_target()
    {
        Assert.False(CynoTargets.IsTarget(31000005, 11000031, -1));
        Assert.True(CynoTargets.IsTarget(30002086, 10000042, 0.387)); // Turnur, low-sec.
    }
}

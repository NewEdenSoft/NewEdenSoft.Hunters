using NewEdenSoft.Hunters.Contracts.Map;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Widgets.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class JumpRangesTests
{
    /// <summary>Systems along x, at 0, 3, 5, 8, 9 ly; one raised 4 ly; Pochven and Zarzakh in range. Gates 1 - 2 - 3 - 6.</summary>
    private static readonly MapModel Model = MapModel.Build(new MapPack(
        1,
        [new MapRegion(1, "Region"), new MapRegion(10000070, "Pochven")],
        [
            System(1, 0, -0.5),
            System(2, 3, 0.4),
            // Shown as 0.5: high-sec.
            System(3, 5, 0.45),
            System(4, 8, -0.1),
            System(5, 9, -0.1),
            // sqrt(36 + 16) = 7.2 ly.
            System(6, 6, -0.2, height: 4),
            System(7, 2, -1, region: 10000070),
            System(30100000, 1, -1),
        ],
        [new MapGate(1, 2), new MapGate(2, 3), new MapGate(3, 6)]));

    private static MapSystem System(int id, double x, double security, double height = 0, int region = 1) =>
        new(id, $"S{id}", region, 1, security, new MapPoint(x, 0), new MapPoint(x, 0), height);

    private static string[] Names(bool[] inRange) => [.. inRange.Select((v, i) => v ? Model.Names[i] : null).OfType<string>()];

    [Fact]
    public void Reaches_the_cyno_targets_within_the_range_in_3d()
    {
        Assert.Equal(["S2", "S4", "S6"], Names(JumpRanges.InRange(Model, 0, 8)));
        Assert.Equal(["S2"], Names(JumpRanges.InRange(Model, 0, 7)));
    }

    [Fact]
    public void Leaves_out_high_sec_Pochven_Zarzakh_and_the_origin()
    {
        Assert.False(JumpRanges.IsCynoTarget(Model, Model.IndexOf[3]));
        Assert.False(JumpRanges.IsCynoTarget(Model, Model.IndexOf[7]));
        Assert.False(JumpRanges.IsCynoTarget(Model, Model.IndexOf[30100000]));
        Assert.False(JumpRanges.InRange(Model, 1, 10)[1]);
    }

    [Fact]
    public void The_hover_line_tells_the_distance_and_whether_a_jump_drive_lands_there()
    {
        var inRange = JumpRanges.InRange(Model, 0, 8);

        Assert.Equal(new MapDistanceInfo(3, "7.21 ly", "S1", "in range", true), MapDistanceInfo.Create(Model, 0, Model.IndexOf[6], inRange));
        Assert.Equal(new MapDistanceInfo(null, "9 ly", "S1", "out of range", false), MapDistanceInfo.Create(Model, 0, Model.IndexOf[5], inRange));
        Assert.Equal(new MapDistanceInfo(2, "5 ly", "S1", "no cyno", false), MapDistanceInfo.Create(Model, 0, Model.IndexOf[3], inRange));
    }

    [Fact]
    public void Without_the_jump_range_the_hover_line_tells_only_the_distance()
    {
        var distance = MapDistanceInfo.Create(Model, 0, Model.IndexOf[2], inRange: null);

        Assert.Equal(new MapDistanceInfo(1, "3 ly", "S1"), distance);
        Assert.Equal("", distance.StatusText);
    }

    [Theory]
    [InlineData(null, "no gates")]
    [InlineData(1, "1 jump")]
    [InlineData(5, "5 jumps")]
    public void Counts_the_gate_jumps(int? jumps, string text) => Assert.Equal(text, new MapDistanceInfo(jumps, "1 ly", "S1").JumpsText);

    [Fact]
    public void Shows_whole_ranges_plainly_and_others_with_decimals()
    {
        Assert.Equal("8 ly", JumpRanges.FormatLy(8));
        Assert.Equal("7.50 ly", JumpRanges.FormatLy(7.5));
        Assert.Equal("12.3 ly", JumpRanges.FormatLy(12.25));
    }
}

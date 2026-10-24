using Avalonia.Media;
using NewEdenSoft.Hunters.Contracts.Activity;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class MapFilterTests
{
    private static readonly Color Quiet = Colors.DimGray;

    private readonly ThemeCatalog _catalog = ThemeCatalog.LoadEmbedded();
    private readonly MapModel _model = MapModel.Build(MapTestData.Pack());

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(99, 1)]
    [InlineData(100, 2)]
    [InlineData(5000, 5)]
    public void Heat_groups_are_the_band_a_value_reaches(int value, int group) =>
        Assert.Equal(group, MapFilters.HeatGroup(value, [1, 100, 250, 500, 1000]));

    [Fact]
    public void Npc_kills_are_coloured_by_band_and_sized()
    {
        // Jita 0, Perimeter 120, Niyabainen 1200, the rest 1.
        var filter = Create(MapFilterId.Npc, Activity(last: [0, 120, 1200, 1, 1, 1, 1]));

        Assert.Null(filter.Notice);
        Assert.Equal([0, 2, 5, 1, 1, 1, 1], filter.Groups.Select(g => (int)g));
        Assert.Equal(Quiet, filter.Colours[0]);
        Assert.Equal(["0", "1+", "100+", "250+", "500+", "1000+"], filter.Legend.Select(l => l.Label));
        Assert.Equal(2f, filter.SizeOf(2));
        Assert.Equal(0.8f, filter.SizeOf(0));
    }

    [Fact]
    public void Rising_and_falling_compare_with_the_hour_before_above_the_minimum()
    {
        var activity = Activity(last: [300, 300, 10, 0, 0, 0, 0], previous: [100, 500, 0, 0, 0, 0, 0]);

        Assert.Equal([4, 0, 1, 0, 0, 0, 0], Create(MapFilterId.NpcRising, activity).Groups.Select(g => (int)g));
        Assert.Equal([0, 4, 0, 0, 0, 0, 0], Create(MapFilterId.NpcFalling, activity).Groups.Select(g => (int)g));
        // Niyabainen's +10 is below the minimum of 25 kills.
        Assert.Equal([4, 0, 0, 0, 0, 0, 0], Create(MapFilterId.NpcRising, activity, minNpc: 25).Groups.Select(g => (int)g));
        Assert.Equal("−50", Create(MapFilterId.NpcFalling, activity).Legend[2].Label);
    }

    [Fact]
    public void Without_data_every_system_is_quiet_with_a_reason()
    {
        Assert.Equal("No activity data from the server yet.", Create(MapFilterId.Npc, null).Notice);
        Assert.Contains("another map version", Create(MapFilterId.Ship, Activity(last: [1, 1, 1, 1, 1, 1, 1]) with { SdeBuild = 7 }).Notice);
        Assert.Contains("hour before is missing", Create(MapFilterId.NpcRising, Activity(last: [1, 1, 1, 1, 1, 1, 1])).Notice);
        Assert.All(Create(MapFilterId.Jumps, null).Groups, g => Assert.Equal(0, g));
        Assert.Null(Create(MapFilterId.Security, null).Notice);
    }

    private MapFilter Create(MapFilterId id, ActivityCurrent? activity, int minNpc = 0) =>
        MapFilters.Create(id, _model, _catalog, new MapFilterContext(activity, minNpc, Quiet));

    private ActivityCurrent Activity(int[] last, int[]? previous = null)
    {
        var npc = Columns(last, previous);
        var other = Columns(last, previous);
        var metrics = new ActivityMetrics(npc, other, other, other);
        var time = DateTimeOffset.UnixEpoch;
        return new ActivityCurrent(1, _model.SdeBuild, time, time, 24, 24, metrics, metrics);
    }

    private static ActivityColumns Columns(int[] last, int[]? previous) =>
        new(last, previous, last, null, [.. last.Select(_ => (double?)null)], [.. last.Select(_ => (int?)null)], [.. last.Select(_ => (int?)null)]);
}

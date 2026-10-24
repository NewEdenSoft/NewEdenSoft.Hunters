using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class MapModelTests
{
    private readonly MapModel _model = MapModel.Build(MapTestData.Pack());

    [Fact]
    public void Gates_are_index_pairs_and_skip_unknown_systems()
    {
        Assert.Equal(4 * 2, _model.Gates.Length);
        Assert.Equal(_model.IndexOf[10], _model.Gates[0]);
        Assert.Equal(_model.IndexOf[11], _model.Gates[1]);
    }

    [Fact]
    public void Light_years_are_measured_in_3d_with_the_height()
    {
        var pack = MapTestData.Pack();
        // Perimeter 1 ly east of Jita on the top-down map, and raised 2 ly.
        var raised = pack with { Systems = [.. pack.Systems.Select(s => s.Id == 11 ? s with { Height = 2 } : s)] };
        var model = MapModel.Build(raised);

        Assert.Equal(Math.Sqrt(5), model.LyBetween(model.IndexOf[10], model.IndexOf[11]), 5);
        Assert.Equal(0, model.LyBetween(model.IndexOf[10], model.IndexOf[10]));
    }

    [Fact]
    public void Positions_and_bounds_follow_the_layout()
    {
        var jita = _model.IndexOf[11];

        Assert.Equal(1, _model.Positions(MapLayout.TopDown)[jita * 2]);
        Assert.Equal(2, _model.Positions(MapLayout.Flat)[jita * 2]);
        Assert.Equal(new MapBounds(-40, -40, 40, 40), _model.Bounds(MapLayout.TopDown));
        Assert.Equal(new MapBounds(-80, -80, 80, 80), _model.Bounds(MapLayout.Flat));
    }

    [Fact]
    public void Regions_are_largest_first_and_labelled_at_the_mean_of_their_systems()
    {
        Assert.Equal(MapTestData.Forge, _model.Regions[0].Id);

        var lonetrek = _model.Regions.Single(r => r.Id == MapTestData.Lonetrek);
        Assert.Equal((10.5f, 0f), lonetrek.Centre(MapLayout.TopDown));
        Assert.Equal((21f, 0f), lonetrek.Centre(MapLayout.Flat));
    }

    [Fact]
    public void Scattered_regions_get_no_label()
    {
        var pochven = _model.Regions.Single(r => r.Id == MapTestData.Pochven);

        Assert.Null(pochven.Centre(MapLayout.TopDown));
        Assert.Null(pochven.Centre(MapLayout.Flat));
    }

    [Fact]
    public void Region_names_are_looked_up_by_system()
    {
        Assert.Equal("Lonetrek", _model.RegionName(_model.IndexOf[20]));
    }
}

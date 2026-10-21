using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;
using SkiaSharp;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class MapSceneTests
{
    private static readonly MapPalette Palette = new(
        SKColors.Black, SKColors.Gray, SKColors.White, SKColors.Silver, SKColors.White, SKColors.Orange);

    private readonly ThemeCatalog _catalog = ThemeCatalog.LoadEmbedded();
    private readonly MapModel _model = MapModel.Build(MapTestData.Pack());

    [Fact]
    public void Security_filter_groups_systems_by_bucket()
    {
        var filter = MapFilters.Security(_model, _catalog);

        Assert.Equal(9, filter.Groups[_model.IndexOf[10]]);
        Assert.Equal(1, filter.Groups[_model.IndexOf[12]]);
        Assert.Equal(SecurityStatus.NullBucket, filter.Groups[_model.IndexOf[20]]);
        Assert.Equal(SecurityStatus.BucketCount, filter.Colours.Count);
        Assert.Equal(["1.0", "0.9", "0.8", "0.7", "0.6", "0.5", "0.4", "0.3", "0.2", "0.1", "0.0", "Null"], filter.Legend.Select(l => l.Label));
    }

    [Fact]
    public void Hit_test_finds_the_system_under_the_cursor_and_skips_hidden_ones()
    {
        var security = MapFilters.Security(_model, _catalog);
        var groups = (byte[])security.Groups.Clone();
        groups[_model.IndexOf[11]] = MapFilter.Hidden;
        var scene = Scene(security with { Groups = groups });
        var camera = new MapCamera(Scale: 40, OffsetX: 100, OffsetY: 100, Width: 400, Height: 300);

        // Jita at world (0, 0), Perimeter (hidden) at (1, 0), in the top-down layout.
        Assert.Equal(_model.IndexOf[10], scene.HitTest(camera, 103, 98));
        Assert.Equal(-1, scene.HitTest(camera, 140, 100));
    }

    [Theory]
    [InlineData(40f)]
    [InlineData(200f)]
    public void Draws_systems_in_their_filter_colour(float scale)
    {
        var filter = MapFilters.Security(_model, _catalog);
        var scene = Scene(filter);
        var camera = new MapCamera(scale, OffsetX: 200, OffsetY: 150, Width: 400, Height: 300);
        using var surface = SKSurface.Create(new SKImageInfo(400, 300));

        scene.Draw(surface.Canvas, camera, hovered: _model.IndexOf[10]);

        using var image = surface.Snapshot();
        using var pixels = image.PeekPixels();
        var jita = filter.Colours[filter.Groups[_model.IndexOf[10]]];
        Assert.Equal(new SKColor(jita.R, jita.G, jita.B), pixels.GetPixelColor(200, 150));
        Assert.Equal(Palette.Background, pixels.GetPixelColor(5, 295));
    }

    private MapScene Scene(MapFilter filter) =>
        new(_model, MapLayout.TopDown, filter, Palette, new MapFonts(SKTypeface.Default, SKTypeface.Default));
}

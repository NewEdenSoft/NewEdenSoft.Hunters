using NewEdenSoft.Hunters.Desktop.Characters;
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
    public void Draws_systems_in_their_filter_colour_over_the_background(float scale)
    {
        var filter = MapFilters.Security(_model, _catalog);
        var scene = Scene(filter);
        var camera = new MapCamera(scale, OffsetX: 200, OffsetY: 150, Width: 400, Height: 300);
        using var surface = SKSurface.Create(new SKImageInfo(400, 300));
        surface.Canvas.Clear(SKColors.Navy);

        scene.Draw(surface.Canvas, camera, hovered: _model.IndexOf[10]);

        using var image = surface.Snapshot();
        using var pixels = image.PeekPixels();
        var jita = filter.Colours[filter.Groups[_model.IndexOf[10]]];
        Assert.Equal(new SKColor(jita.R, jita.G, jita.B), pixels.GetPixelColor(200, 150));
        Assert.Equal(SKColors.Navy, pixels.GetPixelColor(5, 295));
    }

    [Fact]
    public void Margins_cut_gates_but_not_the_background()
    {
        var scene = Scene(MapFilters.Security(_model, _catalog));
        // Zoomed in far enough for the full margin; Jita at (200, 150), Perimeter 200 px to the right.
        var camera = new MapCamera(Scale: 200, OffsetX: 200, OffsetY: 150, Width: 400, Height: 300);
        using var surface = SKSurface.Create(new SKImageInfo(400, 300));
        surface.Canvas.Clear(SKColors.Navy);

        scene.Draw(surface.Canvas, camera, hovered: -1);

        using var image = surface.Snapshot();
        using var pixels = image.PeekPixels();
        var radius = MapScene.SystemRadius(200);
        // In the margin next to Jita, on the line of the gate to Perimeter: background, not gate.
        Assert.Equal(SKColors.Navy, pixels.GetPixelColor(200 + (int)radius + 3, 150));
        // Halfway along the gate: drawn.
        Assert.NotEqual(SKColors.Navy, pixels.GetPixelColor(300, 150));
    }

    [Fact]
    public void Markers_are_drawn_up_and_to_the_right_of_their_system()
    {
        var scene = Scene(MapFilters.Security(_model, _catalog));
        var camera = new MapCamera(Scale: 40, OffsetX: 200, OffsetY: 150, Width: 400, Height: 300);
        // A diamond is hollow, so the middle of its badge shows the badge's fill (the halo colour).
        var diamond = new MapMarker(new CharacterMarker(MarkerKind.Icon, "diamond"), IsOnline: true);
        var radius = MapScene.SystemRadius(40);
        var badgeCentre = (X: (int)(200 + radius + 12 + 9), Y: (int)(150 - radius - 12 - 9));

        SKColor Sample(IReadOnlyList<MapMarkerGroup>? markers)
        {
            using var surface = SKSurface.Create(new SKImageInfo(400, 300));
            surface.Canvas.Clear(SKColors.Navy);
            scene.Draw(surface.Canvas, camera, hovered: -1, markers);
            using var image = surface.Snapshot();
            using var pixels = image.PeekPixels();
            return pixels.GetPixelColor(badgeCentre.X, badgeCentre.Y);
        }

        Assert.Equal(SKColors.Navy, Sample(null));
        Assert.Equal(Palette.Halo, Sample([new MapMarkerGroup(_model.IndexOf[10], [diamond])]));
    }

    private MapScene Scene(MapFilter filter) =>
        new(_model, MapLayout.TopDown, filter, Palette, new MapFonts(SKTypeface.Default, SKTypeface.Default));
}

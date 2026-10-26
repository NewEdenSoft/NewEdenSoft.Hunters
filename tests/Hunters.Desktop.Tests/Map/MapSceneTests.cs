using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;
using SkiaSharp;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class MapSceneTests
{
    private static readonly MapPalette Palette = new(
        SKColors.Black, SKColors.Gray, SKColors.White, SKColors.Silver, SKColors.White, SKColors.Orange, Selection: SKColors.Lime,
        JumpRange: SKColors.Blue);

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
        var diamond = new MapMarker(90000001, new CharacterMarker(MarkerKind.Icon, "diamond"), IsOnline: true);
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

    [Fact]
    public void The_focused_system_is_ringed_and_hoverable_even_when_hidden()
    {
        var security = MapFilters.Security(_model, _catalog);
        var groups = (byte[])security.Groups.Clone();
        var jita = _model.IndexOf[10];
        groups[jita] = MapFilter.Hidden;
        var scene = Scene(security with { Groups = groups });
        var camera = new MapCamera(Scale: 40, OffsetX: 200, OffsetY: 150, Width: 400, Height: 300);
        var radius = MapScene.SystemRadius(40);

        SKColor Sample(int focused, int x, int y)
        {
            using var surface = SKSurface.Create(new SKImageInfo(400, 300));
            surface.Canvas.Clear(SKColors.Navy);
            scene.Draw(surface.Canvas, camera, hovered: -1, focused: focused);
            using var image = surface.Snapshot();
            using var pixels = image.PeekPixels();
            return pixels.GetPixelColor(x, y);
        }

        // The inner ring above Jita, and its dot, only when focused.
        var ringY = (int)MathF.Round(150 - radius - 5);
        Assert.Equal(SKColors.Navy, Sample(-1, 200, ringY));
        Assert.NotEqual(SKColors.Navy, Sample(jita, 200, ringY));
        Assert.Equal(Palette.Accent, Sample(jita, 200, 150));

        Assert.Equal(-1, scene.HitTest(camera, 200, 150));
        Assert.Equal(jita, scene.HitTest(camera, 200, 150, focused: jita));
        Assert.Equal(jita, scene.HitTest(camera, 200, 150, selection: jita));
    }

    [Fact]
    public void The_selection_is_a_crosshair_apart_from_the_focus()
    {
        var scene = Scene(MapFilters.Security(_model, _catalog));
        var camera = new MapCamera(Scale: 40, OffsetX: 200, OffsetY: 150, Width: 400, Height: 300);
        var jita = _model.IndexOf[10];
        // On the upper tick (no gate goes up from Jita), past the focus's outer ring.
        var tickY = (int)MathF.Round(150 - MapScene.SystemRadius(40) - 12);

        SKColor Sample(int focused, int selection)
        {
            using var surface = SKSurface.Create(new SKImageInfo(400, 300));
            surface.Canvas.Clear(SKColors.Navy);
            scene.Draw(surface.Canvas, camera, hovered: -1, focused: focused, selection: selection);
            using var image = surface.Snapshot();
            using var pixels = image.PeekPixels();
            return pixels.GetPixelColor(200, tickY);
        }

        Assert.Equal(SKColors.Navy, Sample(-1, -1));
        // Mostly the selection colour: the 1.5 px tick only partly covers the pixel.
        Assert.True(Sample(-1, jita).Green > 160);
        // The focus marks it already.
        Assert.Equal(SKColors.Navy, Sample(jita, jita));
    }

    [Fact]
    public void Systems_in_jump_range_get_a_blue_ring_and_glow()
    {
        var scene = Scene(MapFilters.Security(_model, _catalog));
        var camera = new MapCamera(Scale: 40, OffsetX: 200, OffsetY: 150, Width: 400, Height: 300);
        var inRange = new bool[_model.Count];
        inRange[_model.IndexOf[12]] = true;
        // Niyabainen at (200, 190); its only gate goes up to Jita, so its left side is clear.
        var radius = MapScene.SystemRadius(40);
        var ringX = (int)MathF.Round(200 - radius - 2);
        var glowX = (int)MathF.Round(200 - radius - 6);

        SKColor Sample(bool[]? jumpRange, int x)
        {
            using var surface = SKSurface.Create(new SKImageInfo(400, 300));
            surface.Canvas.Clear(SKColors.Black);
            scene.Draw(surface.Canvas, camera, hovered: -1, jumpRange: jumpRange);
            using var image = surface.Snapshot();
            using var pixels = image.PeekPixels();
            return pixels.GetPixelColor(x, 190);
        }

        Assert.Equal(SKColors.Black, Sample(null, ringX));
        Assert.Equal(SKColors.Black, Sample(null, glowX));
        // Blue only: the ring mostly covers its pixel, the glow fades out towards its edge.
        Assert.True(Sample(inRange, ringX) is { Red: 0, Green: 0, Blue: > 150 });
        Assert.True(Sample(inRange, glowX) is { Red: 0, Green: 0, Blue: > 0 });
    }

    [Fact]
    public void A_route_is_drawn_along_its_gates_in_the_route_colour()
    {
        var scene = new MapScene(
            _model, MapLayout.TopDown, MapFilters.Security(_model, _catalog), Palette with { Route = SKColors.Cyan },
            new MapFonts(SKTypeface.Default, SKTypeface.Default));
        var camera = new MapCamera(Scale: 40, OffsetX: 100, OffsetY: 150, Width: 400, Height: 300);
        // Jita (0, 0) to Otela (10, 0) by Perimeter (1, 0): a straight line along y = 150, from x 100 to 500.
        int[] route = [_model.IndexOf[10], _model.IndexOf[11], _model.IndexOf[20]];

        SKColor Sample(MapRoute? shown, int x, int y = 150)
        {
            using var surface = SKSurface.Create(new SKImageInfo(400, 300));
            surface.Canvas.Clear(SKColors.Black);
            scene.Draw(surface.Canvas, camera, hovered: -1, route: shown);
            using var image = surface.Snapshot();
            using var pixels = image.PeekPixels();
            return pixels.GetPixelColor(x, y);
        }

        // Between Perimeter and Otela, on a bar (12 px past Perimeter's centre, after 2 periods of 14), and in a gap.
        Assert.True(Sample(new MapRoute(route), 140 + 28 + 4) is { Red: < 60, Green: > 200, Blue: > 200 });
        Assert.True(Sample(new MapRoute(route), 140 + 28 + 11) is { Green: < 120 });
        // The bars move along: 7 px on, the gap is where a bar was.
        Assert.True(Sample(new MapRoute(route, Travelled: 7), 140 + 28 + 4) is { Green: < 120 });
        // The soft stroke reaches past the bars' width.
        Assert.True(Sample(new MapRoute(route), 300, 153) is { Red: 0, Green: > 0 });
        Assert.Equal(SKColors.Black, Sample(null, 300, 153));
    }

    [Fact]
    public void Clicks_find_the_marker_that_was_drawn()
    {
        var scene = Scene(MapFilters.Security(_model, _catalog));
        var camera = new MapCamera(Scale: 40, OffsetX: 200, OffsetY: 150, Width: 400, Height: 300);
        var letter = new CharacterMarker(MarkerKind.Letter, "A");
        MapMarkerGroup[] groups = [new(_model.IndexOf[10], [new MapMarker(1, letter, true), new MapMarker(2, letter, false)])];
        var radius = MapScene.SystemRadius(40);
        var (left, bottom) = (200 + radius + 12, 150 - radius - 12);

        // Badges are 18 px with 3 px between them, along the bottom of the row.
        Assert.Equal(1, scene.HitTestMarker(camera, groups, left + 9, bottom - 9));
        Assert.Equal(2, scene.HitTestMarker(camera, groups, left + 21 + 9, bottom - 9));
        Assert.Null(scene.HitTestMarker(camera, groups, left + 19.5f, bottom - 9));
        Assert.Null(scene.HitTestMarker(camera, groups, 200, 150));
    }

    private MapScene Scene(MapFilter filter) =>
        new(_model, MapLayout.TopDown, filter, Palette, new MapFonts(SKTypeface.Default, SKTypeface.Default));
}

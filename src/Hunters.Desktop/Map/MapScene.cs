using SkiaSharp;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>Theme colours the map is drawn with. <paramref name="Halo"/> outlines region names so they read over gates.</summary>
public sealed record MapPalette(SKColor Halo, SKColor Gate, SKColor Region, SKColor Label, SKColor LabelHover, SKColor Hover);

/// <summary>Typefaces for system names and region names.</summary>
public sealed record MapFonts(SKTypeface Label, SKTypeface Region);

/// <summary>
/// The view onto the map: <paramref name="Scale"/> is the zoom in pixels per light year, the
/// offsets place world (0, 0) on screen, and the size is the control's, all in device-independent pixels.
/// </summary>
public readonly record struct MapCamera(float Scale, float OffsetX, float OffsetY, float Width, float Height)
{
    public float ToScreenX(float worldX) => worldX * Scale + OffsetX;

    public float ToScreenY(float worldY) => worldY * Scale + OffsetY;
}

/// <summary>
/// Everything needed to draw the map in one layout, filter and theme, prepared once and reused for
/// every frame (docs/decisions.md, D21). Immutable once built, so frames can be drawn on Avalonia's
/// render thread while the UI thread builds the next scene. Follows
/// <c>web/hunters-web/src/map/MapRenderer.ts</c>; the sizes and rules are the same.
/// </summary>
/// <remarks>
/// The background is left alone, so the map shows over whatever is behind it: a panel in the main
/// window, or an overlay at the user's opacity. Each frame: gates in view (solid within a constellation, dashed between constellations and
/// regions, coloured from one system's colour to the other's) under the view transform; a margin
/// around each system when zoomed in; systems in view, batched by colour group; then system names
/// to the bottom right of each system when zoomed in, or region names when zoomed out.
/// </remarks>
public sealed class MapScene
{
    /// <summary>Zoom (pixels per light year) from which system names are drawn; below it, region names are.</summary>
    public const float LabelZoom = 28;

    public const float HoverRadius = 10;

    /// <summary>Dash patterns in pixels, by gate style: within a constellation (solid), between constellations, between regions.</summary>
    private static readonly float[][] GateDashes = [[], [6, 4], [18, 8]];

    /// <summary>Sizes on screen (pixels) grow with the zoom (pixels per light year) between these limits.</summary>
    private const float SystemRadiusPerZoom = 0.06f, SystemRadiusMin = 0.9f, SystemRadiusMax = 6.5f;

    private const float GateWidthBase = 0.25f, GateWidthPerZoom = 0.02f, GateWidthMin = 0.4f, GateWidthMax = 2f;

    /// <summary>Clear space between a system and the gates around it, opening from <see cref="MarginFromZoom"/>.</summary>
    private const float SystemMarginPerZoom = 0.15f, SystemMarginMax = 5f, MarginFromZoom = 12f;

    /// <summary>Gates are drawn a little transparent so the systems stay the brightest thing on the map.</summary>
    private const float GateAlpha = 0.55f;

    private const float LabelFontSize = 11, RegionFontSize = 12, RegionLetterSpacing = 2;

    /// <summary>Clear space around a region name, so neighbouring names never read as one.</summary>
    private const float RegionGap = 14;

    private const float LabelCell = 6;

    private readonly MapModel _model;
    private readonly float[] _xy;
    private readonly MapPalette _palette;
    private readonly SKColor[] _groupColours;
    private readonly int[][] _byGroup;
    private readonly GridIndex _grid;

    /// <summary>Gates whose ends share a colour, per gate style: endpoints in world coordinates and the colour.</summary>
    private readonly List<PlainGate>[] _plainGates;

    /// <summary>Gates between two colours, per gate style, each with its gradient in world coordinates.</summary>
    private readonly List<GradientGate>[] _gradientGates;

    private readonly SKFont _labelFont;
    private readonly SKFont _regionFont;
    private readonly float[] _labelWidths;
    private readonly float _labelAscent;
    private readonly RegionText?[] _regionTexts;

    public MapScene(MapModel model, MapLayout layout, MapFilter filter, MapPalette palette, MapFonts fonts)
    {
        _model = model;
        Layout = layout;
        Filter = filter;
        _xy = model.Positions(layout);
        _palette = palette;
        _groupColours = filter.Colours.Select(c => new SKColor(c.R, c.G, c.B, c.A)).ToArray();
        _byGroup = GroupSystems(filter);
        _grid = new GridIndex(_xy, 1);
        (_plainGates, _gradientGates) = BuildGates();

        _labelFont = new SKFont(fonts.Label, LabelFontSize) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
        _labelAscent = -_labelFont.Metrics.Ascent;
        _labelWidths = model.Names.Select(name => _labelFont.MeasureText(name)).ToArray();

        _regionFont = new SKFont(fonts.Region, RegionFontSize) { Subpixel = true };
        _regionTexts = model.Regions.Select(r => r.Centre(layout) is null ? null : RegionText.Create(r.Name.ToUpperInvariant(), _regionFont)).ToArray();
    }

    public MapModel Model => _model;

    public MapLayout Layout { get; }

    public MapFilter Filter { get; }

    public MapBounds Bounds => _model.Bounds(Layout);

    public float X(int index) => _xy[index * 2];

    public float Y(int index) => _xy[index * 2 + 1];

    /// <summary>The visible system nearest to a point on screen, within <see cref="HoverRadius"/> pixels, or -1.</summary>
    public int HitTest(in MapCamera camera, float screenX, float screenY)
    {
        var worldX = (screenX - camera.OffsetX) / camera.Scale;
        var worldY = (screenY - camera.OffsetY) / camera.Scale;
        return _grid.Nearest(worldX, worldY, HoverRadius / camera.Scale, i => Filter.Groups[i] != MapFilter.Hidden);
    }

    public void Draw(SKCanvas canvas, in MapCamera camera, int hovered)
    {
        // Gates go on a layer of their own, so the margins can be cut out of them without cutting
        // through what is behind the map.
        canvas.SaveLayer();
        DrawGates(canvas, camera);
        DrawMargins(canvas, camera);
        canvas.Restore();

        DrawSystems(canvas, camera, hovered);
        if (camera.Scale >= LabelZoom)
            DrawLabels(canvas, camera, hovered);
        else
            DrawRegionLabels(canvas, camera);
    }

    // Gates are drawn in world coordinates, so widths and dashes are divided by the zoom to stay the same on screen.
    private void DrawGates(SKCanvas canvas, in MapCamera camera)
    {
        var scale = camera.Scale;
        var view = new SKRect(-camera.OffsetX / scale, -camera.OffsetY / scale, (camera.Width - camera.OffsetX) / scale, (camera.Height - camera.OffsetY) / scale);

        canvas.Save();
        canvas.Translate(camera.OffsetX, camera.OffsetY);
        canvas.Scale(scale);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = GateWidth(scale) / scale,
        };
        var alpha = (byte)Math.Round(GateAlpha * 255);
        var paths = new Dictionary<SKColor, SKPath>();

        for (var style = 0; style < GateDashes.Length; style++)
        {
            using var dash = GateDashes[style].Length == 0 ? null : SKPathEffect.CreateDash(GateDashes[style].Select(d => d / scale).ToArray(), 0);
            paint.PathEffect = dash;

            // Gates whose ends share a colour: one path per colour, only the gates in view.
            foreach (var gate in _plainGates[style])
            {
                if (!gate.IsIn(view))
                    continue;
                if (!paths.TryGetValue(gate.Colour, out var path))
                    paths[gate.Colour] = path = new SKPath();
                path.MoveTo(gate.X1, gate.Y1);
                path.LineTo(gate.X2, gate.Y2);
            }

            paint.Shader = null;
            foreach (var (colour, path) in paths)
            {
                paint.Color = colour.WithAlpha((byte)(colour.Alpha * alpha / 255));
                canvas.DrawPath(path, paint);
                path.Dispose();
            }

            paths.Clear();

            paint.Color = SKColors.Black.WithAlpha(alpha);
            foreach (var gate in _gradientGates[style])
            {
                if (!gate.Line.IsIn(view))
                    continue;
                paint.Shader = gate.Gradient;
                canvas.DrawLine(gate.Line.X1, gate.Line.Y1, gate.Line.X2, gate.Line.Y2, paint);
            }

            paint.Shader = null;
        }

        canvas.Restore();
    }

    /// <summary>
    /// Zoomed in, clears a ring around each system so gates stop short of it: one batched cut-out of
    /// the gate layer, cheaper than shortening every gate per frame.
    /// </summary>
    private void DrawMargins(SKCanvas canvas, in MapCamera camera)
    {
        var margin = SystemMargin(camera.Scale);
        if (margin <= 0)
            return;

        var radius = SystemRadius(camera.Scale) + margin;
        using var path = new SKPath();
        foreach (var systems in _byGroup)
            AddCircles(path, systems, camera, radius, radius + 2);
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstOut };
        canvas.DrawPath(path, paint);
    }

    // Systems keep a size in pixels, so they are drawn in screen coordinates.
    private void DrawSystems(SKCanvas canvas, in MapCamera camera, int hovered)
    {
        var radius = SystemRadius(camera.Scale);
        var pad = radius + 2;
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var path = new SKPath();

        for (var group = 0; group < _byGroup.Length; group++)
        {
            path.Reset();
            AddCircles(path, _byGroup[group], camera, radius, pad);
            paint.Color = _groupColours[group];
            canvas.DrawPath(path, paint);
        }

        if (hovered >= 0)
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1.5f;
            paint.Color = _palette.Hover;
            canvas.DrawCircle(camera.ToScreenX(X(hovered)), camera.ToScreenY(Y(hovered)), radius + 4, paint);
        }
    }

    private void AddCircles(SKPath path, int[] systems, in MapCamera camera, float radius, float pad)
    {
        foreach (var i in systems)
        {
            var sx = camera.ToScreenX(X(i));
            var sy = camera.ToScreenY(Y(i));
            if (sx < -pad || sy < -pad || sx > camera.Width + pad || sy > camera.Height + pad)
                continue;
            path.AddCircle(sx, sy, radius);
        }
    }

    /// <summary>System names to the bottom right of each system in view, skipping any that would overlap one already drawn.</summary>
    private void DrawLabels(SKCanvas canvas, in MapCamera camera, int hovered)
    {
        var radius = SystemRadius(camera.Scale);
        var lineHeight = LabelFontSize + 2;
        var taken = new HashSet<long>();
        using var paint = new SKPaint { IsAntialias = true };

        // The hovered system always gets its label first.
        if (hovered >= 0)
            Place(canvas, camera, hovered, _palette.LabelHover, force: true);
        for (var i = 0; i < _model.Count; i++)
        {
            if (i != hovered && Filter.Groups[i] != MapFilter.Hidden)
                Place(canvas, camera, i, _palette.Label, force: false);
        }

        void Place(SKCanvas canvas, in MapCamera camera, int i, SKColor colour, bool force)
        {
            var x = camera.ToScreenX(X(i)) + radius + 2;
            var y = camera.ToScreenY(Y(i)) + radius;
            if (x > camera.Width || y > camera.Height || x < -200 || y < -lineHeight)
                return;

            var width = _labelWidths[i];
            if (!Claim(taken, x, y, x + width, y + lineHeight, force))
                return;

            paint.Color = colour;
            canvas.DrawText(_model.Names[i], x, y + _labelAscent, _labelFont, paint);
        }
    }

    /// <summary>
    /// Region names at the centre of their systems, when zoomed out too far for system names. Larger
    /// regions are placed first; a name that would overlap one already drawn is skipped.
    /// </summary>
    private void DrawRegionLabels(SKCanvas canvas, in MapCamera camera)
    {
        var taken = new HashSet<long>();
        var height = RegionFontSize + 4;
        using var halo = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            StrokeJoin = SKStrokeJoin.Round,
            Color = _palette.Halo,
        };
        using var fill = new SKPaint { IsAntialias = true, Color = _palette.Region };

        for (var r = 0; r < _model.Regions.Count; r++)
        {
            if (_regionTexts[r] is not { } text || _model.Regions[r].Centre(Layout) is not { } centre)
                continue;

            var x = camera.ToScreenX(centre.X);
            var y = camera.ToScreenY(centre.Y);
            var half = text.Width / 2;
            if (x + half < 0 || x - half > camera.Width || y < -height || y > camera.Height + height)
                continue;
            if (!Claim(taken, x - half - RegionGap, y - height / 2f - RegionGap / 2, x + half + RegionGap, y + height / 2f + RegionGap / 2, force: false))
                continue;

            // Centred on (x, y): the blob's origin is the start of its baseline.
            var originX = x - half;
            var originY = y + text.MiddleToBaseline;
            canvas.DrawText(text.Blob, originX, originY, halo);
            canvas.DrawText(text.Blob, originX, originY, fill);
        }
    }

    /// <summary>Marks the cells of a rectangle as taken, unless one already is (and <paramref name="force"/> is false).</summary>
    private static bool Claim(HashSet<long> taken, float left, float top, float right, float bottom, bool force)
    {
        var x0 = (int)MathF.Floor(left / LabelCell);
        var x1 = (int)MathF.Floor(right / LabelCell);
        var y0 = (int)MathF.Floor(top / LabelCell);
        var y1 = (int)MathF.Floor(bottom / LabelCell);

        if (!force)
        {
            for (var cx = x0; cx <= x1; cx++)
                for (var cy = y0; cy <= y1; cy++)
                    if (taken.Contains(CellKey(cx, cy)))
                        return false;
        }

        for (var cx = x0; cx <= x1; cx++)
            for (var cy = y0; cy <= y1; cy++)
                taken.Add(CellKey(cx, cy));
        return true;
    }

    private static long CellKey(int cx, int cy) => ((long)cx << 32) | (uint)cy;

    public static float SystemRadius(float scale) => Math.Clamp(scale * SystemRadiusPerZoom, SystemRadiusMin, SystemRadiusMax);

    /// <summary>Gate width in pixels: thin zoomed out, so the map is not a tangle of lines.</summary>
    public static float GateWidth(float scale) => Math.Clamp(GateWidthBase + scale * GateWidthPerZoom, GateWidthMin, GateWidthMax);

    public static float SystemMargin(float scale) => Math.Clamp((scale - MarginFromZoom) * SystemMarginPerZoom, 0, SystemMarginMax);

    private int[][] GroupSystems(MapFilter filter)
    {
        var lists = filter.Colours.Select(_ => new List<int>()).ToArray();
        for (var i = 0; i < filter.Groups.Length; i++)
        {
            if (filter.Groups[i] != MapFilter.Hidden)
                lists[filter.Groups[i]].Add(i);
        }

        return lists.Select(l => l.ToArray()).ToArray();
    }

    private (List<PlainGate>[] Plain, List<GradientGate>[] Gradients) BuildGates()
    {
        var gates = _model.Gates;
        var groups = Filter.Groups;
        var plain = GateDashes.Select(_ => new List<PlainGate>()).ToArray();
        var gradients = GateDashes.Select(_ => new List<GradientGate>()).ToArray();

        for (var g = 0; g < gates.Length; g += 2)
        {
            var from = gates[g];
            var to = gates[g + 1];
            var style = _model.RegionIds[from] != _model.RegionIds[to] ? 2
                : _model.ConstellationIds[from] != _model.ConstellationIds[to] ? 1
                : 0;
            var line = new PlainGate(X(from), Y(from), X(to), Y(to), default);

            // A gate to a hidden system keeps the neutral gate colour.
            var hidden = groups[from] == MapFilter.Hidden || groups[to] == MapFilter.Hidden;
            var fromColour = hidden ? _palette.Gate : _groupColours[groups[from]];
            var toColour = hidden ? _palette.Gate : _groupColours[groups[to]];

            if (fromColour == toColour)
            {
                plain[style].Add(line with { Colour = fromColour });
            }
            else
            {
                var gradient = SKShader.CreateLinearGradient(
                    new SKPoint(line.X1, line.Y1), new SKPoint(line.X2, line.Y2), [fromColour, toColour], SKShaderTileMode.Clamp);
                gradients[style].Add(new GradientGate(line, gradient));
            }
        }

        return (plain, gradients);
    }

    private readonly record struct PlainGate(float X1, float Y1, float X2, float Y2, SKColor Colour)
    {
        public bool IsIn(SKRect view) =>
            Math.Max(X1, X2) >= view.Left && Math.Min(X1, X2) <= view.Right &&
            Math.Max(Y1, Y2) >= view.Top && Math.Min(Y1, Y2) <= view.Bottom;
    }

    private sealed record GradientGate(PlainGate Line, SKShader Gradient);

    /// <summary>A region name laid out once, with the letter spacing Skia does not apply by itself.</summary>
    private sealed record RegionText(SKTextBlob Blob, float Width, float MiddleToBaseline)
    {
        public static RegionText? Create(string text, SKFont font)
        {
            var glyphs = font.GetGlyphs(text);
            if (glyphs.Length == 0)
                return null;

            var widths = font.GetGlyphWidths(glyphs);
            var positions = new SKPoint[glyphs.Length];
            float x = 0;
            for (var i = 0; i < glyphs.Length; i++)
            {
                positions[i] = new SKPoint(x, 0);
                x += widths[i] + RegionLetterSpacing;
            }

            using var builder = new SKTextBlobBuilder();
            var run = builder.AllocatePositionedRun(font, glyphs.Length);
            glyphs.CopyTo(run.Glyphs);
            positions.CopyTo(run.Positions);
            var blob = builder.Build();
            if (blob is null)
                return null;

            // Baseline below the vertical middle of capital letters.
            var metrics = font.Metrics;
            return new RegionText(blob, x - RegionLetterSpacing, metrics.CapHeight / 2);
        }
    }
}

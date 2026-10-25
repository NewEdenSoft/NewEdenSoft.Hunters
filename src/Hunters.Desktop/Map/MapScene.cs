using System.Collections.Concurrent;
using NewEdenSoft.Hunters.Desktop.Characters;
using SkiaSharp;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>
/// Theme colours the map is drawn with. <paramref name="Halo"/> outlines region names so they read
/// over gates and fills marker badges; <paramref name="Accent"/> draws the hover ring, the focused
/// system's rings and name, and the markers; <paramref name="HuntGlow"/> is the glow behind systems
/// where a shown hunting profile is likely now; <paramref name="Selection"/> draws the selected system's
/// crosshair and name; <paramref name="JumpRange"/> marks the systems in jump range.
/// </summary>
public sealed record MapPalette(
    SKColor Halo, SKColor Gate, SKColor Region, SKColor Label, SKColor LabelHover, SKColor Accent, SKColor HuntGlow = default, SKColor Selection = default,
    SKColor JumpRange = default);

/// <summary>One character's marker; offline characters are drawn dimmed at their last known system.</summary>
public readonly record struct MapMarker(long CharacterId, CharacterMarker Marker, bool IsOnline);

/// <summary>The markers of the characters in one system, drawn side by side at the end of one leader line.</summary>
public sealed record MapMarkerGroup(int SystemIndex, IReadOnlyList<MapMarker> Markers);

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
/// to the bottom right of each system when zoomed in, or region names when zoomed out; last, the
/// characters' markers, joined by a line to the upper right of their systems. The focused system
/// gets two rings and always its name, in the accent colour; the selected system a ring with four
/// ticks and always its name, in the text colour. With the jump range on, each system in range gets a
/// blue glow under the hunting glow and a thin blue ring over its dot. Shown hunting profiles add a glow under
/// the systems and, zoomed in, a row of badges below each system's name.
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

    /// <summary>Marker badges: size, space between them, and how far the leader line reaches up and right from the system.</summary>
    private const float MarkerSize = 18, MarkerGap = 3, MarkerLead = 12, MarkerCut = 4, MarkerFontSize = 11;

    /// <summary>Hunting badges show from this zoom (pixels per light year); further out, the glow alone does.</summary>
    public const float HuntBadgeZoom = 50;

    /// <summary>Hunting badges: smaller than the character markers, in a row below the system's name.</summary>
    private const float HuntBadge = 17, HuntGap = 3, HuntCut = 4, HuntFontSize = 10, HuntFaintAlpha = 0.4f;

    /// <summary>Offline characters' markers are drawn at this opacity.</summary>
    private const float OfflineAlpha = 0.45f;

    /// <summary>Marker icons, parsed once.</summary>
    private static readonly ConcurrentDictionary<string, SKPath> IconPaths = new();

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
    private readonly SKFont _markerFont;
    private readonly SKFont _huntFont;
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
        _markerFont = new SKFont(fonts.Region, MarkerFontSize) { Subpixel = true };
        _huntFont = new SKFont(fonts.Region, HuntFontSize) { Subpixel = true, Embolden = true };
        _regionTexts = model.Regions.Select(r => r.Centre(layout) is null ? null : RegionText.Create(r.Name.ToUpperInvariant(), _regionFont)).ToArray();
    }

    public MapModel Model => _model;

    public MapLayout Layout { get; }

    public MapFilter Filter { get; }

    public MapBounds Bounds => _model.Bounds(Layout);

    public float X(int index) => _xy[index * 2];

    public float Y(int index) => _xy[index * 2 + 1];

    /// <summary>
    /// The visible system nearest to a point on screen, within <see cref="HoverRadius"/> pixels, or -1.
    /// The focused and selected systems count as visible, as they are drawn even when hidden.
    /// </summary>
    public int HitTest(in MapCamera camera, float screenX, float screenY, int focused = -1, int selection = -1)
    {
        var worldX = (screenX - camera.OffsetX) / camera.Scale;
        var worldY = (screenY - camera.OffsetY) / camera.Scale;
        return _grid.Nearest(worldX, worldY, HoverRadius / camera.Scale, i => Filter.Groups[i] != MapFilter.Hidden || i == focused || i == selection);
    }

    /// <param name="focused">The focused system's index, or -1.</param>
    /// <param name="hunting">The shown hunting profiles' glow and badges, or null.</param>
    /// <param name="selection">The selected system's index, or -1.</param>
    /// <param name="jumpRange">Per system, whether it is in jump range of the focused system; null when the range isn't shown.</param>
    public void Draw(
        SKCanvas canvas, in MapCamera camera, int hovered, IReadOnlyList<MapMarkerGroup>? markers = null, int focused = -1, HuntingLayer? hunting = null,
        int selection = -1, bool[]? jumpRange = null)
    {
        // The selection is drawn only apart from the focus, which already marks the system.
        if (selection == focused)
            selection = -1;

        // Gates go on a layer of their own, so the margins can be cut out of them without cutting
        // through what is behind the map.
        canvas.SaveLayer();
        DrawGates(canvas, camera);
        DrawMargins(canvas, camera);
        canvas.Restore();

        if (jumpRange is not null)
            DrawJumpRange(canvas, camera, jumpRange);
        if (hunting is not null)
            DrawHuntGlow(canvas, camera, hunting.Glow);
        DrawSystems(canvas, camera);
        // Over the systems, so the range shows on busy (large, bright) systems too.
        if (jumpRange is not null)
            DrawJumpRangeRings(canvas, camera, jumpRange);
        if (hovered >= 0)
            DrawHover(canvas, camera, hovered);
        if (focused >= 0)
            DrawFocused(canvas, camera, focused);
        if (selection >= 0)
            DrawSelection(canvas, camera, selection);

        if (camera.Scale >= LabelZoom)
        {
            // Badge rows first, so names can keep clear of them.
            var huntRows = hunting is { Badges.Count: > 0 } && camera.Scale >= HuntBadgeZoom ? LayoutHuntRows(camera, hunting.Badges) : [];
            DrawLabels(canvas, camera, hovered, focused, selection, huntRows);
            if (huntRows.Count > 0)
                DrawHuntBadges(canvas, camera, huntRows);
        }
        else
        {
            DrawRegionLabels(canvas, camera);
            // The focused and selected systems keep their names when zoomed out, where no other system has one.
            if (focused >= 0)
                DrawLabelZoomedOut(canvas, camera, focused, _palette.Accent);
            if (selection >= 0)
                DrawLabelZoomedOut(canvas, camera, selection, _palette.Selection);
        }

        if (markers is { Count: > 0 })
            DrawMarkers(canvas, camera, markers);
    }

    /// <summary>
    /// Each group: a leader line from the system's edge up and to the right, ending at the bottom-left
    /// corner of a row of badges. Drawn in screen coordinates at every zoom, over everything else. A
    /// row that would overlap one already drawn (neighbouring systems, zoomed out) moves further up.
    /// </summary>
    private void DrawMarkers(SKCanvas canvas, in MapCamera camera, IReadOnlyList<MapMarkerGroup> groups)
    {
        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var content = new SKPaint { IsAntialias = true };
        using var icon = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };

        foreach (var row in LayoutMarkers(camera, groups))
        {
            var group = row.Group;
            var anyOnline = group.Markers.Any(m => m.IsOnline);
            line.Color = Faded(_palette.Accent, anyOnline);
            canvas.DrawLine(row.LineX, row.LineY, row.AnchorX, row.AnchorY, line);

            for (var k = 0; k < group.Markers.Count; k++)
            {
                var marker = group.Markers[k];
                var rect = row.Badge(k);
                var (left, top) = (rect.Left, rect.Top);
                using var badge = ChamferPath(left, top, MarkerSize, MarkerCut);
                fill.Color = _palette.Halo;
                canvas.DrawPath(badge, fill);
                border.Color = Faded(_palette.Accent, marker.IsOnline);
                canvas.DrawPath(badge, border);

                var colour = Faded(_palette.Accent, marker.IsOnline);
                if (marker.Marker.IconPath is { } data)
                {
                    // Icons are 2-unit strokes in a 24 x 24 box, drawn at 60% of the badge.
                    var path = IconPaths.GetOrAdd(data, d => SKPath.ParseSvgPathData(d) ?? new SKPath());
                    var size = MarkerSize * 0.6f;
                    canvas.Save();
                    canvas.Translate(left + (MarkerSize - size) / 2, top + (MarkerSize - size) / 2);
                    canvas.Scale(size / 24);
                    icon.Color = colour;
                    icon.StrokeWidth = 2.4f;
                    canvas.DrawPath(path, icon);
                    canvas.Restore();
                }
                else if (marker.Marker.Text is { } text)
                {
                    content.Color = colour;
                    var width = _markerFont.MeasureText(text);
                    canvas.DrawText(text, left + (MarkerSize - width) / 2, top + MarkerSize / 2 + _markerFont.Metrics.CapHeight / 2, _markerFont, content);
                }
            }
        }
    }

    /// <summary>The character whose marker badge is under a point on screen, or null.</summary>
    public long? HitTestMarker(in MapCamera camera, IReadOnlyList<MapMarkerGroup> groups, float screenX, float screenY)
    {
        // Rows drawn later are on top, so look from the last.
        var rows = LayoutMarkers(camera, groups);
        for (var r = rows.Count - 1; r >= 0; r--)
        {
            for (var k = 0; k < rows[r].Group.Markers.Count; k++)
            {
                if (rows[r].Badge(k).Contains(screenX, screenY))
                    return rows[r].Group.Markers[k].CharacterId;
            }
        }

        return null;
    }

    /// <summary>
    /// Where each group's line and badges go, in drawing order. Shared by drawing and hit-testing so
    /// a click finds the badge that was drawn. Groups out of view are left out.
    /// </summary>
    private List<MarkerRow> LayoutMarkers(in MapCamera camera, IReadOnlyList<MapMarkerGroup> groups)
    {
        var radius = SystemRadius(camera.Scale);
        var edge = radius * MathF.Sqrt(0.5f);
        var rows = new List<MarkerRow>();
        foreach (var group in groups)
        {
            var sx = camera.ToScreenX(X(group.SystemIndex));
            var sy = camera.ToScreenY(Y(group.SystemIndex));
            var anchorX = sx + radius + MarkerLead;
            var anchorY = sy - radius - MarkerLead;
            var rowWidth = group.Markers.Count * (MarkerSize + MarkerGap) - MarkerGap;
            if (sx > camera.Width || anchorX + rowWidth < 0 || anchorY - MarkerSize > camera.Height || sy < 0)
                continue;

            // Step up past rows already placed; the line stays straight up and to the right.
            var bounds = new SKRect(anchorX, anchorY - MarkerSize, anchorX + rowWidth, anchorY);
            for (var step = 0; step < 8 && rows.Any(r => r.Bounds.IntersectsWith(bounds)); step++)
            {
                var lift = MarkerSize + MarkerGap;
                anchorX += lift;
                anchorY -= lift;
                bounds.Offset(lift, -lift);
            }

            rows.Add(new MarkerRow(group, sx + edge, sy - edge, anchorX, anchorY, bounds));
        }

        return rows;
    }

    /// <summary>One group's leader line (from the system's edge to the anchor) and its row of badges.</summary>
    private readonly record struct MarkerRow(MapMarkerGroup Group, float LineX, float LineY, float AnchorX, float AnchorY, SKRect Bounds)
    {
        public SKRect Badge(int k)
        {
            var left = AnchorX + k * (MarkerSize + MarkerGap);
            return new SKRect(left, AnchorY - MarkerSize, left + MarkerSize, AnchorY);
        }
    }

    private static SKColor Faded(SKColor colour, bool online) =>
        online ? colour : colour.WithAlpha((byte)(colour.Alpha * OfflineAlpha));

    /// <summary>A square with the top-left and bottom-right corners cut off, the HUD's badge shape.</summary>
    private static SKPath ChamferPath(float left, float top, float size, float cut)
    {
        var path = new SKPath();
        path.MoveTo(left + cut, top);
        path.LineTo(left + size, top);
        path.LineTo(left + size, top + size - cut);
        path.LineTo(left + size - cut, top + size);
        path.LineTo(left, top + size);
        path.LineTo(left, top + cut);
        path.Close();
        return path;
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

        var radius = SystemRadius(camera.Scale);
        using var path = new SKPath();
        for (var group = 0; group < _byGroup.Length; group++)
        {
            var r = radius * GroupSize(group) + margin;
            AddCircles(path, _byGroup[group], camera, r, r + 2);
        }

        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstOut };
        canvas.DrawPath(path, paint);
    }

    // Systems keep a size in pixels, so they are drawn in screen coordinates.
    private void DrawSystems(SKCanvas canvas, in MapCamera camera)
    {
        var radius = SystemRadius(camera.Scale);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var path = new SKPath();

        // Groups in order, so with sizes the busier (larger) systems are drawn over the quiet ones.
        for (var group = 0; group < _byGroup.Length; group++)
        {
            var r = radius * GroupSize(group);
            path.Reset();
            AddCircles(path, _byGroup[group], camera, r, r + 2);
            paint.Color = _groupColours[group];
            canvas.DrawPath(path, paint);
        }
    }

    private void DrawHover(SKCanvas canvas, in MapCamera camera, int hovered)
    {
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = _palette.Accent };
        canvas.DrawCircle(camera.ToScreenX(X(hovered)), camera.ToScreenY(Y(hovered)), SystemRadius(camera.Scale) * Filter.SizeOf(hovered) + 4, paint);
    }

    /// <summary>
    /// A cool blue behind each system a jump drive can reach from the focused system: a soft disc, so a
    /// range reads as one blue area zoomed out and as marked systems zoomed in. Under the hunting glow.
    /// It reaches past the system's drawn size, so the activity colours' larger systems keep their blue.
    /// </summary>
    private void DrawJumpRange(SKCanvas canvas, in MapCamera camera, bool[] inRange)
    {
        var radius = SystemRadius(camera.Scale);
        var colour = _palette.JumpRange;
        SKColor[] stops = [colour.WithAlpha((byte)(255 * 0.8f)), colour.WithAlpha((byte)(255 * 0.45f)), colour.WithAlpha(0)];
        float[] positions = [0, 0.5f, 1];
        using var paint = new SKPaint { IsAntialias = true };
        for (var i = 0; i < inRange.Length && i < _model.Count; i++)
        {
            if (!inRange[i])
                continue;

            var drawn = radius * Filter.SizeOf(i);
            var reach = Math.Max(Math.Max(drawn * 3, drawn + 8), 9);
            var sx = camera.ToScreenX(X(i));
            var sy = camera.ToScreenY(Y(i));
            if (sx < -reach || sy < -reach || sx > camera.Width + reach || sy > camera.Height + reach)
                continue;

            using var shader = SKShader.CreateRadialGradient(new SKPoint(sx, sy), reach, stops, positions, SKShaderTileMode.Clamp);
            paint.Shader = shader;
            canvas.DrawCircle(sx, sy, reach, paint);
        }
    }

    /// <summary>
    /// A thin blue ring just outside each system in jump range, over the systems: whatever colour and size
    /// the filter draws a system in, the ring shows it is in range. One path for all of them.
    /// </summary>
    private void DrawJumpRangeRings(SKCanvas canvas, in MapCamera camera, bool[] inRange)
    {
        var radius = SystemRadius(camera.Scale);
        using var path = new SKPath();
        for (var i = 0; i < inRange.Length && i < _model.Count; i++)
        {
            if (!inRange[i])
                continue;

            var r = radius * Filter.SizeOf(i) + 2;
            var sx = camera.ToScreenX(X(i));
            var sy = camera.ToScreenY(Y(i));
            if (sx < -r || sy < -r || sx > camera.Width + r || sy > camera.Height + r)
                continue;
            path.AddCircle(sx, sy, r);
        }

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = _palette.JumpRange };
        canvas.DrawPath(path, paint);
    }

    /// <summary>Two rings around the focused system; its dot too if the filter hides it.</summary>
    private void DrawFocused(SKCanvas canvas, in MapCamera camera, int focused)
    {
        var radius = SystemRadius(camera.Scale) * Filter.SizeOf(focused);
        var (sx, sy) = (camera.ToScreenX(X(focused)), camera.ToScreenY(Y(focused)));
        using var paint = new SKPaint { IsAntialias = true, Color = _palette.Accent };
        if (Filter.Groups[focused] == MapFilter.Hidden)
            canvas.DrawCircle(sx, sy, radius, paint);

        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 2;
        canvas.DrawCircle(sx, sy, radius + 5, paint);
        paint.StrokeWidth = 1;
        canvas.DrawCircle(sx, sy, radius + 9, paint);
    }

    /// <summary>
    /// The selected system: one ring with four ticks around it, like a crosshair, in the text colour, so
    /// it reads apart from the focus's double ring. Its dot too if the filter hides it.
    /// </summary>
    private void DrawSelection(SKCanvas canvas, in MapCamera camera, int selection)
    {
        var radius = SystemRadius(camera.Scale) * Filter.SizeOf(selection);
        var (sx, sy) = (camera.ToScreenX(X(selection)), camera.ToScreenY(Y(selection)));
        using var paint = new SKPaint { IsAntialias = true, Color = _palette.Selection };
        if (Filter.Groups[selection] == MapFilter.Hidden)
            canvas.DrawCircle(sx, sy, radius, paint);

        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1.5f;
        canvas.DrawCircle(sx, sy, radius + 5, paint);
        foreach (var (dx, dy) in Ticks)
            canvas.DrawLine(sx + dx * (radius + 8), sy + dy * (radius + 8), sx + dx * (radius + 13), sy + dy * (radius + 13), paint);
    }

    /// <summary>The directions of the selection's ticks.</summary>
    private static readonly (int X, int Y)[] Ticks = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>The focused or selected system's name when zoomed out, to the bottom right of its rings.</summary>
    private void DrawLabelZoomedOut(SKCanvas canvas, in MapCamera camera, int index, SKColor colour)
    {
        var radius = SystemRadius(camera.Scale);
        using var paint = new SKPaint { IsAntialias = true, Color = colour };
        canvas.DrawText(
            _model.Names[index],
            camera.ToScreenX(X(index)) + radius + 10,
            camera.ToScreenY(Y(index)) + radius + 4 + _labelAscent,
            _labelFont,
            paint);
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
    /// <summary>
    /// A soft glow behind each system where a shown hunting profile is likely now: brighter and wider the
    /// stronger it is. Under the system dots, over the gates.
    /// </summary>
    private void DrawHuntGlow(SKCanvas canvas, in MapCamera camera, float[] glow)
    {
        var radius = SystemRadius(camera.Scale);
        var colour = _palette.HuntGlow;
        using var paint = new SKPaint { IsAntialias = true };
        for (var i = 0; i < glow.Length && i < _model.Count; i++)
        {
            var g = glow[i];
            if (g <= 0)
                continue;

            var sx = camera.ToScreenX(X(i));
            var sy = camera.ToScreenY(Y(i));
            var reach = Math.Max(radius * 3.2f, 9) * (0.7f + 0.5f * g);
            if (sx < -reach || sy < -reach || sx > camera.Width + reach || sy > camera.Height + reach)
                continue;

            using var shader = SKShader.CreateRadialGradient(
                new SKPoint(sx, sy), reach, [colour.WithAlpha((byte)(255 * (0.25f + 0.6f * g))), colour.WithAlpha(0)], SKShaderTileMode.Clamp);
            paint.Shader = shader;
            canvas.DrawCircle(sx, sy, reach, paint);
        }
    }

    /// <summary>
    /// The shown hunting profiles seen at each system, as small badges in each profile's colour, in a row
    /// below the system's name with a short line from the system. Faint where the profile is not usually
    /// there at this hour. Drawn only when names are.
    /// </summary>
    private void DrawHuntBadges(SKCanvas canvas, in MapCamera camera, List<HuntRow> rows)
    {
        var edge = SystemRadius(camera.Scale) * MathF.Sqrt(0.5f);
        using var line = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = _palette.Halo };
        using var border = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var text = new SKPaint { IsAntialias = true };
        using var icon = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };

        foreach (var row in rows)
        {
            var (left, top) = (row.Bounds.Left, row.Bounds.Top);
            var anyNow = row.Group.Badges.Any(b => !b.Faint);
            line.Color = Hunt(row.Group.Badges[0].Colour, !anyNow);
            canvas.DrawLine(row.X + edge, row.Y + edge, left, top + HuntBadge / 2, line);

            for (var k = 0; k < row.Group.Badges.Count; k++)
            {
                var badge = row.Group.Badges[k];
                var x = left + k * (HuntBadge + HuntGap);
                using var shape = ChamferPath(x, top, HuntBadge, HuntCut);
                canvas.DrawPath(shape, fill);
                var colour = Hunt(badge.Colour, badge.Faint);
                border.Color = colour;
                canvas.DrawPath(shape, border);

                if (badge.Icon is { } data)
                {
                    // Icons are strokes in a 24 x 24 box, drawn at 85% of the badge.
                    var path = IconPaths.GetOrAdd(data, d => SKPath.ParseSvgPathData(d) ?? new SKPath());
                    var size = HuntBadge * 0.85f;
                    canvas.Save();
                    canvas.Translate(x + (HuntBadge - size) / 2, top + (HuntBadge - size) / 2);
                    canvas.Scale(size / 24);
                    icon.Color = colour;
                    icon.StrokeWidth = 1.9f;
                    canvas.DrawPath(path, icon);
                    canvas.Restore();
                }
                else
                {
                    text.Color = colour;
                    var width = _huntFont.MeasureText(badge.Code);
                    canvas.DrawText(badge.Code, x + (HuntBadge - width) / 2, top + HuntBadge / 2 + _huntFont.Metrics.CapHeight / 2, _huntFont, text);
                }
            }
        }

        static SKColor Hunt(Theming.Rgba c, bool faint) =>
            new(c.R8, c.G8, c.B8, (byte)(c.A8 * (faint ? HuntFaintAlpha : 1)));
    }

    /// <summary>
    /// Where each system's badge row goes, below its name: rows out of view are left out, and a row that
    /// would overlap one already placed (neighbouring systems) is left out too.
    /// </summary>
    private List<HuntRow> LayoutHuntRows(in MapCamera camera, IReadOnlyList<HuntingBadgeGroup> groups)
    {
        var radius = SystemRadius(camera.Scale);
        var rows = new List<HuntRow>();
        foreach (var group in groups)
        {
            var sx = camera.ToScreenX(X(group.SystemIndex));
            var sy = camera.ToScreenY(Y(group.SystemIndex));
            var left = sx + radius + 4;
            var top = sy + radius + LabelFontSize + 5;
            var bounds = new SKRect(left, top, left + group.Badges.Count * (HuntBadge + HuntGap) - HuntGap, top + HuntBadge);
            if (bounds.Left > camera.Width || bounds.Right < 0 || bounds.Top > camera.Height || bounds.Bottom < 0)
                continue;
            if (rows.Any(r => r.Bounds.IntersectsWith(bounds)))
                continue;
            rows.Add(new HuntRow(group, sx, sy, bounds));
        }

        return rows;
    }

    /// <summary>One system's hunting badge row: the system on screen and the row's bounds.</summary>
    private readonly record struct HuntRow(HuntingBadgeGroup Group, float X, float Y, SKRect Bounds);

    private void DrawLabels(SKCanvas canvas, in MapCamera camera, int hovered, int focused, int selection, List<HuntRow> huntRows)
    {
        var radius = SystemRadius(camera.Scale);
        var lineHeight = LabelFontSize + 2;
        var taken = new HashSet<long>();
        using var paint = new SKPaint { IsAntialias = true };

        // The hovered, focused and selected systems always get their labels first.
        if (hovered >= 0)
            Place(canvas, camera, hovered, _palette.LabelHover, force: true);
        if (focused >= 0 && focused != hovered)
            Place(canvas, camera, focused, _palette.Accent, force: true);
        if (selection >= 0 && selection != hovered)
            Place(canvas, camera, selection, _palette.Selection, force: true);

        // Hunting badges: their systems' names next, then no name goes where the badges are drawn.
        var first = new HashSet<int>();
        foreach (var row in huntRows)
        {
            first.Add(row.Group.SystemIndex);
            if (row.Group.SystemIndex != hovered && row.Group.SystemIndex != focused && row.Group.SystemIndex != selection)
                Place(canvas, camera, row.Group.SystemIndex, _palette.Label, force: false);
        }

        foreach (var row in huntRows)
            Claim(taken, row.Bounds.Left, row.Bounds.Top, row.Bounds.Right, row.Bounds.Bottom, force: true);

        for (var i = 0; i < _model.Count; i++)
        {
            if (i != hovered && i != focused && i != selection && !first.Contains(i) && Filter.Groups[i] != MapFilter.Hidden)
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

    private float GroupSize(int group) => Filter.Sizes is { } sizes && group < sizes.Count ? sizes[group] : 1;

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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Platform;
using SkiaSharp;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>A hovered system and where it is on screen, relative to the control.</summary>
public readonly record struct MapHover(int Index, Point Position);

/// <summary>A system's menu was asked for (a right click on it), at a point on the control.</summary>
public readonly record struct MapSystemMenuRequest(int Index, Point Position);

/// <summary>A tracked character to mark on the map: where it is, and how it is marked.</summary>
public sealed record MapCharacterMarker(long CharacterId, string Name, int SolarSystemId, CharacterMarker Marker, bool IsOnline);

/// <summary>
/// Draws the map with Skia (docs/decisions.md, D21) and handles pan (drag), zoom (wheel), hover,
/// clicks on character markers and systems (and double clicks on systems), a system's menu (right
/// click), and animated focus on a system. With a focus, it shows the gate route from there to the
/// hovered system, else to <see cref="RouteToSystemId"/>, with bars moving along it.
/// The drawing itself is in <see cref="MapScene"/>; this control owns the camera and the input, and
/// rebuilds the scene when the model, layout, filter or theme colours change. It has no background
/// of its own, so the map shows over its parent (a panel, or an overlay at the user's opacity).
/// </summary>
public sealed class MapControl : Control
{
    public static readonly StyledProperty<MapModel?> ModelProperty =
        AvaloniaProperty.Register<MapControl, MapModel?>(nameof(Model));

    public static readonly StyledProperty<MapLayout> LayoutProperty =
        AvaloniaProperty.Register<MapControl, MapLayout>(nameof(Layout));

    public static readonly StyledProperty<MapFilter?> FilterProperty =
        AvaloniaProperty.Register<MapControl, MapFilter?>(nameof(Filter));

    /// <summary>Characters to mark on the map, at their systems.</summary>
    public static readonly StyledProperty<IReadOnlyList<MapCharacterMarker>?> MarkersProperty =
        AvaloniaProperty.Register<MapControl, IReadOnlyList<MapCharacterMarker>?>(nameof(Markers));

    /// <summary>The focused system: double-ringed and always named. Null for none.</summary>
    public static readonly StyledProperty<int?> FocusedSystemIdProperty =
        AvaloniaProperty.Register<MapControl, int?>(nameof(FocusedSystemId));

    /// <summary>The selected system: a crosshair, and always named. Null for none.</summary>
    public static readonly StyledProperty<int?> SelectedSystemIdProperty =
        AvaloniaProperty.Register<MapControl, int?>(nameof(SelectedSystemId));

    /// <summary>The shown hunting profiles: a glow and badges per system. Null for none.</summary>
    public static readonly StyledProperty<HuntingLayer?> HuntingProperty =
        AvaloniaProperty.Register<MapControl, HuntingLayer?>(nameof(Hunting));

    /// <summary>Per system, whether it is in jump range of the focused system: a blue glow and ring. Null to show none.</summary>
    public static readonly StyledProperty<bool[]?> JumpRangeProperty =
        AvaloniaProperty.Register<MapControl, bool[]?>(nameof(JumpRange));

    /// <summary>
    /// Where the gate route from the focused system goes while no system on the map is hovered (a hovered
    /// one takes over); null for none.
    /// </summary>
    public static readonly StyledProperty<int?> RouteToSystemIdProperty =
        AvaloniaProperty.Register<MapControl, int?>(nameof(RouteToSystemId));

    /// <summary>The highlighted gate route.</summary>
    public static readonly StyledProperty<Color> RouteColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(RouteColor), Colors.DeepSkyBlue);

    /// <summary>The glow and ring of systems in jump range.</summary>
    public static readonly StyledProperty<Color> JumpRangeColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(JumpRangeColor), Colors.DodgerBlue);

    /// <summary>The ring around systems where someone with a routine is usually there at this hour.</summary>
    public static readonly StyledProperty<Color> ReliableColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(ReliableColor), Colors.Aquamarine);

    /// <summary>The glow behind systems where a shown hunting profile is likely now.</summary>
    public static readonly StyledProperty<Color> HuntGlowColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(HuntGlowColor), Colors.Orange);

    /// <summary>Outline around region names, usually the page background.</summary>
    public static readonly StyledProperty<Color> HaloColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(HaloColor), Colors.Black);

    /// <summary>Gates to hidden systems.</summary>
    public static readonly StyledProperty<Color> GateColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(GateColor), Colors.Gray);

    /// <summary>Region names, the hovered system's name, and the selection.</summary>
    public static readonly StyledProperty<Color> TextColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(TextColor), Colors.White);

    /// <summary>System names.</summary>
    public static readonly StyledProperty<Color> MutedTextColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(MutedTextColor), Colors.Gray);

    /// <summary>The ring around the hovered system, and the character markers.</summary>
    public static readonly StyledProperty<Color> HoverColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(HoverColor), Colors.Orange);

    /// <summary>Zoom per wheel notch: e^0.15, about 16%.</summary>
    private const double WheelStep = 0.15;

    /// <summary>Focusing zooms in at least this far (pixels per light year), so system names show.</summary>
    public const float FocusScale = 60;

    private const double FocusMilliseconds = 350;

    /// <summary>A press that moves less than this is a click, not a drag.</summary>
    private const double ClickSlop = 4;

    /// <summary>Two clicks on the same system within this time are a double click.</summary>
    private static readonly TimeSpan DoubleClickTime = TimeSpan.FromMilliseconds(350);

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private static readonly Lazy<MapFonts> Fonts = new(() => new MapFonts(
        LoadTypeface("Saira-Regular.ttf"),
        LoadTypeface("Oxanium-SemiBold.ttf")));

    private MapScene? _scene;
    private float _scale = 1;
    private float _fitScale = 1;
    private float _offsetX;
    private float _offsetY;
    private bool _fitted;
    private int _hovered = -1;
    private Point? _dragFrom;
    private Point _pressedAt;
    private bool _panned;
    private (int SolarSystemId, bool ZoomIn)? _pendingFocus;
    private FocusAnimation? _animation;
    private IReadOnlyList<MapMarkerGroup> _markerGroups = [];

    /// <summary>The gate route shown (system indexes, focus first), and between which systems it was worked out.</summary>
    private int[]? _route;
    private (int From, int To) _routeFor = (-1, -1);

    /// <summary>When the route was shown, for its moving bars; they stand still while the user wants less motion.</summary>
    private long _routeStart;
    private bool _routeStill;

    /// <summary>Animation frames are being asked for, to move the route's bars.</summary>
    private bool _routeTicking;

    /// <summary>The last click on a system and its timestamp (ms), to tell a double click.</summary>
    private (int Index, ulong At)? _lastClick;

    static MapControl()
    {
        ClipToBoundsProperty.OverrideDefaultValue<MapControl>(true);
        FocusableProperty.OverrideDefaultValue<MapControl>(true);
    }

    public MapModel? Model
    {
        get => GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }

    public MapLayout Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public MapFilter? Filter
    {
        get => GetValue(FilterProperty);
        set => SetValue(FilterProperty, value);
    }

    public IReadOnlyList<MapCharacterMarker>? Markers
    {
        get => GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public int? FocusedSystemId
    {
        get => GetValue(FocusedSystemIdProperty);
        set => SetValue(FocusedSystemIdProperty, value);
    }

    public int? SelectedSystemId
    {
        get => GetValue(SelectedSystemIdProperty);
        set => SetValue(SelectedSystemIdProperty, value);
    }

    public HuntingLayer? Hunting
    {
        get => GetValue(HuntingProperty);
        set => SetValue(HuntingProperty, value);
    }

    public int? RouteToSystemId
    {
        get => GetValue(RouteToSystemIdProperty);
        set => SetValue(RouteToSystemIdProperty, value);
    }

    public Color RouteColor
    {
        get => GetValue(RouteColorProperty);
        set => SetValue(RouteColorProperty, value);
    }

    public bool[]? JumpRange
    {
        get => GetValue(JumpRangeProperty);
        set => SetValue(JumpRangeProperty, value);
    }

    public Color JumpRangeColor
    {
        get => GetValue(JumpRangeColorProperty);
        set => SetValue(JumpRangeColorProperty, value);
    }

    public Color ReliableColor
    {
        get => GetValue(ReliableColorProperty);
        set => SetValue(ReliableColorProperty, value);
    }

    public Color HuntGlowColor
    {
        get => GetValue(HuntGlowColorProperty);
        set => SetValue(HuntGlowColorProperty, value);
    }

    public Color HaloColor
    {
        get => GetValue(HaloColorProperty);
        set => SetValue(HaloColorProperty, value);
    }

    public Color GateColor
    {
        get => GetValue(GateColorProperty);
        set => SetValue(GateColorProperty, value);
    }

    public Color TextColor
    {
        get => GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public Color MutedTextColor
    {
        get => GetValue(MutedTextColorProperty);
        set => SetValue(MutedTextColorProperty, value);
    }

    public Color HoverColor
    {
        get => GetValue(HoverColorProperty);
        set => SetValue(HoverColorProperty, value);
    }

    /// <summary>The hovered system changed or moved on screen; null when no system is hovered.</summary>
    public event EventHandler<MapHover?>? HoverChanged;

    /// <summary>A character's marker was clicked; the argument is the character id.</summary>
    public event EventHandler<long>? MarkerClicked;

    /// <summary>The user dragged the map (for example, to stop following a character).</summary>
    public event EventHandler? UserPanned;

    /// <summary>A system was right-clicked: its menu should open there.</summary>
    public event EventHandler<MapSystemMenuRequest>? SystemMenuRequested;

    /// <summary>A click on a system (its index), or on empty space (-1).</summary>
    public event EventHandler<int>? SystemClicked;

    /// <summary>A second click on the same system within <see cref="DoubleClickTime"/>; raised after <see cref="SystemClicked"/>.</summary>
    public event EventHandler<int>? SystemDoubleClicked;

    /// <summary>
    /// Moves the map to a system with a short animation, zooming in to at least <see cref="FocusScale"/>
    /// when <paramref name="zoomIn"/> (otherwise keeping the zoom). Before the map is shown, the
    /// focus waits and is applied without animation.
    /// </summary>
    public void FocusOn(int solarSystemId, bool zoomIn)
    {
        if (_scene is null || !_fitted || Bounds.Width <= 0 || TopLevel.GetTopLevel(this) is not { } top)
        {
            _pendingFocus = (solarSystemId, zoomIn);
            InvalidateVisual();
            return;
        }

        if (!_scene.Model.IndexOf.TryGetValue(solarSystemId, out var index))
            return;

        var (width, height) = ((float)Bounds.Width, (float)Bounds.Height);
        var scale = zoomIn ? ClampScale(Math.Max(_scale, FocusScale)) : _scale;
        _animation = new FocusAnimation(
            _scale, (width / 2 - _offsetX) / _scale, (height / 2 - _offsetY) / _scale,
            scale, _scene.X(index), _scene.Y(index));
        SetHovered(-1);
        top.RequestAnimationFrame(OnAnimationFrame);
    }

    /// <summary>Zooms out to show the whole map.</summary>
    public void Fit()
    {
        FitCamera();
        InvalidateVisual();
    }

    private void FitCamera()
    {
        if (_scene is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        const float margin = 24;
        var b = _scene.Bounds;
        var (width, height) = ((float)Bounds.Width, (float)Bounds.Height);
        _fitScale = Math.Min((width - margin * 2) / Math.Max(b.MaxX - b.MinX, 1), (height - margin * 2) / Math.Max(b.MaxY - b.MinY, 1));
        _scale = _fitScale;
        _offsetX = width / 2 - (b.MinX + b.MaxX) / 2 * _scale;
        _offsetY = height / 2 - (b.MinY + b.MaxY) / 2 * _scale;
        _fitted = true;
    }

    /// <summary>Zooms by a factor around a point on the control.</summary>
    public void ZoomAt(Point at, double factor)
    {
        _animation = null;
        var scale = ClampScale(_scale * (float)factor);
        var applied = scale / _scale;
        _offsetX = (float)(at.X - (at.X - _offsetX) * applied);
        _offsetY = (float)(at.Y - (at.Y - _offsetY) * applied);
        _scale = scale;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        if (_scene is null)
            return;

        if (!_fitted)
            FitCamera();

        // A focus asked for before the map was shown: jump there.
        if (_pendingFocus is { } focus && _fitted && _scene.Model.IndexOf.TryGetValue(focus.SolarSystemId, out var index))
        {
            _pendingFocus = null;
            CentreOn(_scene.X(index), _scene.Y(index), focus.ZoomIn ? ClampScale(Math.Max(_scale, FocusScale)) : _scale);
        }

        var route = _route is { } systems ? new MapRoute(systems, RouteTravelled()) : null;
        context.Custom(new DrawOperation(new Rect(Bounds.Size), _scene, Camera(), _hovered, _markerGroups, FocusedIndex, SelectedIndex, Hunting, JumpRange, route));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ModelProperty || change.Property == LayoutProperty)
        {
            // A new map or layout starts zoomed out.
            _fitted = false;
            RebuildScene();
            GroupMarkers();
        }
        else if (change.Property == MarkersProperty)
        {
            GroupMarkers();
        }
        else if (change.Property == FocusedSystemIdProperty || change.Property == SelectedSystemIdProperty || change.Property == HuntingProperty ||
                 change.Property == JumpRangeProperty || change.Property == RouteToSystemIdProperty)
        {
            UpdateRoute();
            InvalidateVisual();
        }
        else if (change.Property == FilterProperty || change.Property == HaloColorProperty || change.Property == GateColorProperty ||
                 change.Property == TextColorProperty || change.Property == MutedTextColorProperty || change.Property == HoverColorProperty ||
                 change.Property == HuntGlowColorProperty || change.Property == ReliableColorProperty || change.Property == JumpRangeColorProperty || change.Property == RouteColorProperty)
        {
            RebuildScene();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        // Keep the same point of the map in the middle.
        if (_fitted && e.PreviousSize.Width > 0 && e.PreviousSize.Height > 0)
        {
            var centreX = ((float)e.PreviousSize.Width / 2 - _offsetX) / _scale;
            var centreY = ((float)e.PreviousSize.Height / 2 - _offsetY) / _scale;
            _offsetX = (float)e.NewSize.Width / 2 - centreX * _scale;
            _offsetY = (float)e.NewSize.Height / 2 - centreY * _scale;
        }

        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var at = e.GetPosition(this);
        ZoomAt(at, Math.Exp(e.Delta.Y * WheelStep));
        UpdateHover(at);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed && _dragFrom is null)
        {
            // Over a system, its menu; elsewhere nothing.
            var at = e.GetPosition(this);
            var index = SystemAt(at);
            if (index >= 0)
            {
                SystemMenuRequested?.Invoke(this, new MapSystemMenuRequest(index, at));
                e.Handled = true;
            }

            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        _dragFrom = _pressedAt = e.GetPosition(this);
        _panned = false;
        // Keys go to the map (Esc clears the selection), out of the search box.
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var at = e.GetPosition(this);
        if (_dragFrom is not { } from)
        {
            UpdateHover(at);
            Cursor = MarkerAt(at) is null ? null : HandCursor;
            return;
        }

        // Small movements while pressing are still a click.
        if (!_panned)
        {
            if (Math.Abs(at.X - _pressedAt.X) < ClickSlop && Math.Abs(at.Y - _pressedAt.Y) < ClickSlop)
                return;
            _panned = true;
            _animation = null;
            UserPanned?.Invoke(this, EventArgs.Empty);
        }

        _offsetX += (float)(at.X - from.X);
        _offsetY += (float)(at.Y - from.Y);
        _dragFrom = at;
        SetHovered(-1);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is null)
            return;

        _dragFrom = null;
        e.Pointer.Capture(null);
        var at = e.GetPosition(this);
        if (!_panned)
        {
            if (MarkerAt(at) is { } characterId)
                MarkerClicked?.Invoke(this, characterId);
            else
                Click(SystemAt(at), e.Timestamp);
        }

        UpdateHover(at);
    }

    /// <summary>A click on a system (or -1, empty space), and a double click if it is the second in a row.</summary>
    private void Click(int index, ulong timestamp)
    {
        var isDouble = index >= 0 && _lastClick is { } last && last.Index == index && timestamp - last.At < DoubleClickTime.TotalMilliseconds;
        _lastClick = isDouble ? null : (index, timestamp);
        SystemClicked?.Invoke(this, index);
        if (isDouble)
            SystemDoubleClicked?.Invoke(this, index);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragFrom = null;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_dragFrom is null)
            SetHovered(-1);
    }

    private MapCamera Camera() => new(_scale, _offsetX, _offsetY, (float)Bounds.Width, (float)Bounds.Height);

    /// <summary>The focused system's index on the current map, or -1.</summary>
    private int FocusedIndex => IndexOf(FocusedSystemId);

    /// <summary>The selected system's index on the current map, or -1.</summary>
    private int SelectedIndex => IndexOf(SelectedSystemId);

    private int IndexOf(int? systemId) =>
        systemId is { } id && _scene is not null && _scene.Model.IndexOf.TryGetValue(id, out var index) ? index : -1;

    private int SystemAt(Point at) => _scene?.HitTest(Camera(), (float)at.X, (float)at.Y, FocusedIndex, SelectedIndex) ?? -1;

    private float ClampScale(float scale) => Math.Clamp(scale, _fitScale * 0.5f, _fitScale * 80);

    private long? MarkerAt(Point at) =>
        _scene is null || _markerGroups.Count == 0 ? null : _scene.HitTestMarker(Camera(), _markerGroups, (float)at.X, (float)at.Y);

    /// <summary>
    /// Puts a world point in the middle of the control at a zoom. Only moves the camera: callers outside
    /// <see cref="Render"/> invalidate, since invalidating during the render pass throws.
    /// </summary>
    private void CentreOn(float worldX, float worldY, float scale)
    {
        _scale = scale;
        _offsetX = (float)Bounds.Width / 2 - worldX * scale;
        _offsetY = (float)Bounds.Height / 2 - worldY * scale;
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        if (_animation is not { } a)
            return;

        a.Start ??= time;
        var progress = Math.Clamp((time - a.Start.Value).TotalMilliseconds / FocusMilliseconds, 0, 1);
        var t = (float)(1 - Math.Pow(1 - progress, 3));
        // The zoom changes geometrically, so zooming in feels even; the centre moves in a straight line.
        CentreOn(
            a.FromX + (a.ToX - a.FromX) * t,
            a.FromY + (a.ToY - a.FromY) * t,
            a.FromScale * MathF.Pow(a.ToScale / a.FromScale, t));
        InvalidateVisual();

        if (progress < 1)
            TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnAnimationFrame);
        else
            _animation = null;
    }

    /// <summary>From and to: zoom and the world point in the middle.</summary>
    private sealed class FocusAnimation(float fromScale, float fromX, float fromY, float toScale, float toX, float toY)
    {
        public float FromScale => fromScale;
        public float FromX => fromX;
        public float FromY => fromY;
        public float ToScale => toScale;
        public float ToX => toX;
        public float ToY => toY;
        public TimeSpan? Start { get; set; }
    }

    private void RebuildScene()
    {
        _scene = Model is { } model && Filter is { } filter && filter.Groups.Length == model.Count
            ? new MapScene(model, Layout, filter, Palette(), Fonts.Value)
            : null;
        _hovered = -1;
        _routeFor = (-1, -1);
        UpdateRoute();
        HoverChanged?.Invoke(this, null);
        InvalidateVisual();
    }

    /// <summary>
    /// The gate route from the focused system to the hovered one, else to <see cref="RouteToSystemId"/>;
    /// worked out again only when either end changes. While one is shown, frames are asked for to move its bars.
    /// </summary>
    private void UpdateRoute()
    {
        var from = FocusedIndex;
        var to = from < 0 ? -1 : _hovered >= 0 ? _hovered : IndexOf(RouteToSystemId);
        if ((from, to) == _routeFor)
            return;

        _routeFor = (from, to);
        _route = _scene is not null && from >= 0 && to >= 0 && GateRoutes.Route(_scene.Model, from, to) is { Length: > 1 } route ? route : null;
        if (_route is null)
            return;

        _routeStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _routeStill = ReducedMotion.IsPreferred();
        StartRouteFrames();
    }

    /// <summary>How far the route's bars have moved, in pixels.</summary>
    private float RouteTravelled() =>
        _routeStill ? 0 : (float)(System.Diagnostics.Stopwatch.GetElapsedTime(_routeStart).TotalSeconds * MapScene.RouteSpeed);

    private void StartRouteFrames()
    {
        if (_routeTicking || _routeStill || TopLevel.GetTopLevel(this) is not { } top)
            return;
        _routeTicking = true;
        top.RequestAnimationFrame(OnRouteFrame);
    }

    /// <summary>Redraws while a route is shown, so its bars move; stops when none is.</summary>
    private void OnRouteFrame(TimeSpan time)
    {
        if (_route is null || _routeStill || TopLevel.GetTopLevel(this) is not { } top)
        {
            _routeTicking = false;
            return;
        }

        InvalidateVisual();
        top.RequestAnimationFrame(OnRouteFrame);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_route is not null)
            StartRouteFrames();
    }

    /// <summary>Markers by system index, in the order given; characters in systems not on the map are left out.</summary>
    private void GroupMarkers()
    {
        _markerGroups = Model is { } model && Markers is { Count: > 0 } markers
            ? markers
                .Where(m => model.IndexOf.ContainsKey(m.SolarSystemId))
                .GroupBy(m => model.IndexOf[m.SolarSystemId])
                .Select(g => new MapMarkerGroup(g.Key, [.. g.Select(m => new MapMarker(m.CharacterId, m.Marker, m.IsOnline))]))
                .ToList()
            : [];
        InvalidateVisual();
    }

    private MapPalette Palette() => new(
        ToSkia(HaloColor), ToSkia(GateColor), ToSkia(TextColor), ToSkia(MutedTextColor), ToSkia(TextColor), ToSkia(HoverColor), ToSkia(HuntGlowColor),
        ToSkia(TextColor), ToSkia(JumpRangeColor), ToSkia(RouteColor), ToSkia(ReliableColor));

    private void UpdateHover(Point at) => SetHovered(SystemAt(at));

    private void SetHovered(int index)
    {
        if (index != _hovered)
        {
            _hovered = index;
            UpdateRoute();
            InvalidateVisual();
        }

        // Raised on every update, so the panel follows the system while zooming.
        if (_scene is null || index < 0)
        {
            HoverChanged?.Invoke(this, null);
            return;
        }

        var camera = Camera();
        HoverChanged?.Invoke(this, new MapHover(index, new Point(camera.ToScreenX(_scene.X(index)), camera.ToScreenY(_scene.Y(index)))));
    }

    private static SKColor ToSkia(Color c) => new(c.R, c.G, c.B, c.A);

    private static SKTypeface LoadTypeface(string file)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://NewEdenSoft.Hunters.Desktop/Assets/Fonts/{file}"));
        using var data = SKData.Create(stream);
        return SKTypeface.FromData(data) ?? SKTypeface.Default;
    }

    /// <summary>Draws one frame on Avalonia's render thread, from a scene and camera captured on the UI thread.</summary>
    private sealed class DrawOperation(Rect bounds, MapScene scene, MapCamera camera, int hovered, IReadOnlyList<MapMarkerGroup> markers, int focused,
        int selection, HuntingLayer? hunting, bool[]? jumpRange, MapRoute? route) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose()
        {
        }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature)
                return;

            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height));
            scene.Draw(canvas, camera, hovered, markers, focused, hunting, selection, jumpRange, route);
            canvas.Restore();
        }
    }
}

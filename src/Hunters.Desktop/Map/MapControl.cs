using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>A hovered system and where it is on screen, relative to the control.</summary>
public readonly record struct MapHover(int Index, Point Position);

/// <summary>
/// Draws the map with Skia (docs/decisions.md, D21) and handles pan (drag), zoom (wheel) and hover.
/// The drawing itself is in <see cref="MapScene"/>; this control owns the camera and the input, and
/// rebuilds the scene when the model, layout, filter or theme colours change.
/// </summary>
public sealed class MapControl : Control
{
    public static readonly StyledProperty<MapModel?> ModelProperty =
        AvaloniaProperty.Register<MapControl, MapModel?>(nameof(Model));

    public static readonly StyledProperty<MapLayout> LayoutProperty =
        AvaloniaProperty.Register<MapControl, MapLayout>(nameof(Layout));

    public static readonly StyledProperty<MapFilter?> FilterProperty =
        AvaloniaProperty.Register<MapControl, MapFilter?>(nameof(Filter));

    public static readonly StyledProperty<Color> BackgroundColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(BackgroundColor), Colors.Black);

    /// <summary>Gates to hidden systems.</summary>
    public static readonly StyledProperty<Color> GateColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(GateColor), Colors.Gray);

    /// <summary>Region names and the hovered system's name.</summary>
    public static readonly StyledProperty<Color> TextColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(TextColor), Colors.White);

    /// <summary>System names.</summary>
    public static readonly StyledProperty<Color> MutedTextColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(MutedTextColor), Colors.Gray);

    /// <summary>The ring around the hovered system.</summary>
    public static readonly StyledProperty<Color> HoverColorProperty =
        AvaloniaProperty.Register<MapControl, Color>(nameof(HoverColor), Colors.Orange);

    /// <summary>Zoom per wheel notch: e^0.15, about 16%.</summary>
    private const double WheelStep = 0.15;

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

    public Color BackgroundColor
    {
        get => GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
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
        var scale = Math.Clamp(_scale * (float)factor, _fitScale * 0.5f, _fitScale * 80);
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

        context.Custom(new DrawOperation(new Rect(Bounds.Size), _scene, Camera(), _hovered));
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ModelProperty || change.Property == LayoutProperty)
        {
            // A new map or layout starts zoomed out.
            _fitted = false;
            RebuildScene();
        }
        else if (change.Property == FilterProperty || change.Property == BackgroundColorProperty || change.Property == GateColorProperty ||
                 change.Property == TextColorProperty || change.Property == MutedTextColorProperty || change.Property == HoverColorProperty)
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
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _dragFrom = e.GetPosition(this);
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
            return;
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
        UpdateHover(e.GetPosition(this));
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

    private void RebuildScene()
    {
        _scene = Model is { } model && Filter is { } filter && filter.Groups.Length == model.Count
            ? new MapScene(model, Layout, filter, Palette(), Fonts.Value)
            : null;
        _hovered = -1;
        HoverChanged?.Invoke(this, null);
        InvalidateVisual();
    }

    private MapPalette Palette() => new(
        ToSkia(BackgroundColor), ToSkia(GateColor), ToSkia(TextColor), ToSkia(MutedTextColor), ToSkia(TextColor), ToSkia(HoverColor));

    private void UpdateHover(Point at) =>
        SetHovered(_scene?.HitTest(Camera(), (float)at.X, (float)at.Y) ?? -1);

    private void SetHovered(int index)
    {
        if (index != _hovered)
        {
            _hovered = index;
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
    private sealed class DrawOperation(Rect bounds, MapScene scene, MapCamera camera, int hovered) : ICustomDrawOperation
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
            scene.Draw(canvas, camera, hovered);
            canvas.Restore();
        }
    }
}

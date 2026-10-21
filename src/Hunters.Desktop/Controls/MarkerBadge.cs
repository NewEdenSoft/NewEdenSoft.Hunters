using System.Globalization;
using Avalonia;
using Avalonia.Media;
using NewEdenSoft.Hunters.Desktop.Characters;

namespace NewEdenSoft.Hunters.Desktop.Controls;

/// <summary>A character's map marker: its letter, number or icon in a chamfered badge.</summary>
public sealed class MarkerBadge : ChamferBorder
{
    public static readonly StyledProperty<CharacterMarker?> MarkerProperty =
        AvaloniaProperty.Register<MarkerBadge, CharacterMarker?>(nameof(Marker));

    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<MarkerBadge, IBrush?>(nameof(Foreground));

    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<MarkerBadge, FontFamily>(nameof(FontFamily), FontFamily.Default);

    /// <summary>The content's share of the badge's size.</summary>
    private const double ContentScale = 0.6;

    static MarkerBadge()
    {
        AffectsRender<MarkerBadge>(MarkerProperty, ForegroundProperty, FontFamilyProperty);
    }

    public CharacterMarker? Marker
    {
        get => GetValue(MarkerProperty);
        set => SetValue(MarkerProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public FontFamily FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Marker is not { } marker || Foreground is not { } foreground)
            return;

        var size = Math.Min(Bounds.Width, Bounds.Height);
        var content = size * ContentScale;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);

        if (marker.IconPath is { } path)
        {
            // Icons are strokes in a 24 x 24 box.
            var scale = content / 24;
            var geometry = Geometry.Parse(path);
            geometry.Transform = new MatrixTransform(
                Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(centre.X - content / 2, centre.Y - content / 2));
            context.DrawGeometry(null, new Pen(foreground, 2 * scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
            return;
        }

        var text = new FormattedText(
            marker.Text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle.Normal, FontWeight.SemiBold), content, foreground);
        context.DrawText(text, new Point(centre.X - text.Width / 2, centre.Y - text.Height / 2));
    }
}

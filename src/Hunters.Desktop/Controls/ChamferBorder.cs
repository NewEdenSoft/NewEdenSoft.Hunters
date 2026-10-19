using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NewEdenSoft.Hunters.Desktop.Controls;

/// <summary>
/// A border with the top-left and bottom-right corners cut off, the HUD's button and chip shape.
/// </summary>
public class ChamferBorder : Decorator
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<ChamferBorder, IBrush?>(nameof(Background));

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<ChamferBorder, IBrush?>(nameof(BorderBrush));

    public static readonly StyledProperty<double> BorderThicknessProperty =
        AvaloniaProperty.Register<ChamferBorder, double>(nameof(BorderThickness), 1);

    public static readonly StyledProperty<double> CutProperty =
        AvaloniaProperty.Register<ChamferBorder, double>(nameof(Cut), 10);

    static ChamferBorder()
    {
        AffectsRender<ChamferBorder>(BackgroundProperty, BorderBrushProperty, BorderThicknessProperty, CutProperty);
    }

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public IBrush? BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    public double BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    public double Cut
    {
        get => GetValue(CutProperty);
        set => SetValue(CutProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var inset = BorderBrush is null ? 0 : BorderThickness / 2;
        var rect = new Rect(Bounds.Size).Deflate(inset);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var cut = Math.Min(Cut, Math.Min(rect.Width, rect.Height) / 2);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(rect.Left + cut, rect.Top), isFilled: true);
            g.LineTo(new Point(rect.Right, rect.Top));
            g.LineTo(new Point(rect.Right, rect.Bottom - cut));
            g.LineTo(new Point(rect.Right - cut, rect.Bottom));
            g.LineTo(new Point(rect.Left, rect.Bottom));
            g.LineTo(new Point(rect.Left, rect.Top + cut));
            g.EndFigure(isClosed: true);
        }

        var pen = BorderBrush is null || BorderThickness <= 0 ? null : new Pen(BorderBrush, BorderThickness);
        context.DrawGeometry(Background, pen, geometry);
    }
}

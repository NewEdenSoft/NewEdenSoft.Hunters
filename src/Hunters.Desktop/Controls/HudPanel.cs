using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace NewEdenSoft.Hunters.Desktop.Controls;

/// <summary>
/// A panel with a 1px frame and accent brackets on the top-left and bottom-right corners.
/// </summary>
public class HudPanel : Decorator
{
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<HudPanel, IBrush?>(nameof(Background));

    public static readonly StyledProperty<IBrush?> BorderBrushProperty =
        AvaloniaProperty.Register<HudPanel, IBrush?>(nameof(BorderBrush));

    public static readonly StyledProperty<IBrush?> BracketBrushProperty =
        AvaloniaProperty.Register<HudPanel, IBrush?>(nameof(BracketBrush));

    public static readonly StyledProperty<double> BracketLengthProperty =
        AvaloniaProperty.Register<HudPanel, double>(nameof(BracketLength), 12);

    static HudPanel()
    {
        AffectsRender<HudPanel>(BackgroundProperty, BorderBrushProperty, BracketBrushProperty, BracketLengthProperty);
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

    public IBrush? BracketBrush
    {
        get => GetValue(BracketBrushProperty);
        set => SetValue(BracketBrushProperty, value);
    }

    public double BracketLength
    {
        get => GetValue(BracketLengthProperty);
        set => SetValue(BracketLengthProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Background is not null)
            context.FillRectangle(Background, bounds);

        if (BorderBrush is not null)
            context.DrawRectangle(new Pen(BorderBrush, 1), bounds.Deflate(0.5));

        if (BracketBrush is null)
            return;

        var pen = new Pen(BracketBrush, 2);
        var len = Math.Min(BracketLength, Math.Min(bounds.Width, bounds.Height) / 2);
        var (l, t, r, b) = (bounds.Left + 1, bounds.Top + 1, bounds.Right - 1, bounds.Bottom - 1);

        context.DrawLine(pen, new Point(l, t), new Point(l + len, t));
        context.DrawLine(pen, new Point(l, t), new Point(l, t + len));
        context.DrawLine(pen, new Point(r, b), new Point(r - len, b));
        context.DrawLine(pen, new Point(r, b), new Point(r, b - len));
    }
}

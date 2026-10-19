using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace NewEdenSoft.Hunters.Desktop.Controls;

/// <summary>Bottom-right resize handle: three diagonal HUD ticks.</summary>
public class ResizeGrip : Control
{
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<ResizeGrip, IBrush?>(nameof(Foreground));

    static ResizeGrip()
    {
        AffectsRender<ResizeGrip>(ForegroundProperty);
        CursorProperty.OverrideDefaultValue<ResizeGrip>(new Cursor(StandardCursorType.BottomRightCorner));
        WidthProperty.OverrideDefaultValue<ResizeGrip>(16);
        HeightProperty.OverrideDefaultValue<ResizeGrip>(16);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        // Transparent fill so the whole square is hit-testable.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Foreground is null)
            return;

        var pen = new Pen(Foreground, 1);
        var (w, h) = (Bounds.Width - 3, Bounds.Height - 3);
        for (var i = 1; i <= 3; i++)
        {
            var d = i * 4;
            context.DrawLine(pen, new Point(w - d, h), new Point(w, h - d));
        }
    }
}

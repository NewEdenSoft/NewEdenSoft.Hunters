using Avalonia;
using Avalonia.Controls;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

public partial class MapWidgetView : UserControl
{
    /// <summary>Distance from the hovered system to the panel's top-left corner.</summary>
    private const double HoverOffset = 14;

    /// <summary>Below this size the layout and colour controls would cover too much of the map.</summary>
    private static readonly Size ControlsMinSize = new(560, 360);

    public MapWidgetView()
    {
        InitializeComponent();
        Map.HoverChanged += OnHoverChanged;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Controls.Opacity = e.NewSize.Width >= ControlsMinSize.Width && e.NewSize.Height >= ControlsMinSize.Height ? 1 : 0;
        Controls.IsHitTestVisible = Controls.Opacity > 0;
    }

    private void OnHoverChanged(object? sender, MapHover? hover)
    {
        (DataContext as MapWidgetViewModel)?.SetHover(hover?.Index);
        if (hover is { } h)
        {
            Canvas.SetLeft(HoverPanel, h.Position.X + HoverOffset);
            Canvas.SetTop(HoverPanel, h.Position.Y + HoverOffset);
        }
    }
}

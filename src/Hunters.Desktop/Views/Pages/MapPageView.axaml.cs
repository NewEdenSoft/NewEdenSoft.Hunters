using Avalonia.Controls;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

namespace NewEdenSoft.Hunters.Desktop.Views.Pages;

public partial class MapPageView : UserControl
{
    /// <summary>Distance from the hovered system to the panel's top-left corner.</summary>
    private const double HoverOffset = 14;

    public MapPageView()
    {
        InitializeComponent();
        Map.HoverChanged += OnHoverChanged;
    }

    private void OnHoverChanged(object? sender, MapHover? hover)
    {
        (DataContext as MapPageViewModel)?.SetHover(hover?.Index);
        if (hover is { } h)
        {
            Canvas.SetLeft(HoverPanel, h.Position.X + HoverOffset);
            Canvas.SetTop(HoverPanel, h.Position.Y + HoverOffset);
        }
    }
}

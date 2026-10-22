using System.ComponentModel;
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

    private MapWidgetViewModel? _viewModel;

    public MapWidgetView()
    {
        InitializeComponent();
        Map.HoverChanged += OnHoverChanged;
        Map.MarkerClicked += (_, id) => _viewModel?.FocusCharacter(id);
        Map.UserPanned += (_, _) => _viewModel?.StopFollowing();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelChanged;

        _viewModel = DataContext as MapWidgetViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            // A focus asked for while this view did not exist (the map page was not shown).
            if (_viewModel.FocusTarget is { } target)
                Map.FocusOn(target.SolarSystemId, target.ZoomIn);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Controls.Opacity = e.NewSize.Width >= ControlsMinSize.Width && e.NewSize.Height >= ControlsMinSize.Height ? 1 : 0;
        Controls.IsHitTestVisible = Controls.Opacity > 0;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapWidgetViewModel.FocusTarget) && _viewModel?.FocusTarget is { } target)
            Map.FocusOn(target.SolarSystemId, target.ZoomIn);
    }

    private void OnHoverChanged(object? sender, MapHover? hover)
    {
        _viewModel?.SetHover(hover?.Index);
        if (hover is { } h)
        {
            Canvas.SetLeft(HoverPanel, h.Position.X + HoverOffset);
            Canvas.SetTop(HoverPanel, h.Position.Y + HoverOffset);
        }
    }
}

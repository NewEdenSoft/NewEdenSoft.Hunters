using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

public partial class MapWidgetView : UserControl
{
    /// <summary>Distance from the hovered system to the panel's top-left corner.</summary>
    private const double HoverOffset = 14;

    /// <summary>Below this size the search would cover too much of the map (a small overlay).</summary>
    private static readonly Size SearchMinSize = new(300, 220);

    private MapWidgetViewModel? _viewModel;
    private MenuFlyout? _menu;

    public MapWidgetView()
    {
        InitializeComponent();
        Map.HoverChanged += OnHoverChanged;
        Map.MarkerClicked += (_, id) => _viewModel?.FocusCharacter(id);
        Map.UserPanned += (_, _) =>
        {
            _viewModel?.StopFollowing();
            _menu?.Hide();
        };
        Map.SystemMenuRequested += OnSystemMenuRequested;
        Map.SystemClicked += (_, index) =>
        {
            _menu?.Hide();
            _viewModel?.SelectSystem(index);
        };
        Map.SystemDoubleClicked += (_, index) => _viewModel?.DoubleClickSystem(index);
        // The map handles the wheel itself; an open menu closes as it zooms.
        Map.AddHandler(PointerWheelChangedEvent, (_, _) => _menu?.Hide(), handledEventsToo: true);

        // Tunnel, so the arrow keys and Enter reach the results before the text box uses them.
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        SearchResults.Tapped += (_, _) => ChooseHighlighted();
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
        var fits = e.NewSize.Width >= SearchMinSize.Width && e.NewSize.Height >= SearchMinSize.Height;
        SearchArea.Opacity = fits ? 1 : 0;
        SearchArea.IsHitTestVisible = fits;
    }

    /// <summary>
    /// "/" puts the cursor in the search box and Esc clears the selection, unless something else is
    /// being typed in.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Source is TextBox || _viewModel?.IsLoaded != true)
            return;

        if (e.Key == Key.Escape && _viewModel.SelectedSystemId is not null)
        {
            _viewModel.ClearSelection();
            e.Handled = true;
        }
        else if (e.KeySymbol == "/" && SearchArea.IsHitTestVisible)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel?.Search is not { } search)
            return;

        switch (e.Key)
        {
            case Key.Down:
                search.Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                search.Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                ChooseHighlighted();
                e.Handled = true;
                break;
            case Key.Escape:
                search.Clear();
                Map.Focus();
                e.Handled = true;
                break;
        }
    }

    private void ChooseHighlighted()
    {
        if (_viewModel?.Search.ChooseHighlighted() == true)
            Map.Focus();
    }

    /// <summary>A system's menu: focus it, or clear the focus if it is the focused system.</summary>
    private void OnSystemMenuRequested(object? sender, MapSystemMenuRequest request)
    {
        if (_viewModel?.Model is not { } model)
            return;

        var isFocused = _viewModel.FocusedSystemId == model.Ids[request.Index];
        var item = new MenuItem { Header = isFocused ? "Clear focus" : $"Focus {model.Names[request.Index]}" };
        item.Click += (_, _) =>
        {
            if (isFocused)
                _viewModel.ClearSystem();
            else
                _viewModel.FocusSystem(request.Index);
        };

        _menu?.Hide();
        _menu = new MenuFlyout { Items = { item } };
        _menu.ShowAt(Map, showAtPointer: true);
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

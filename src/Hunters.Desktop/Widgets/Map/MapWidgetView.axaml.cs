using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using NewEdenSoft.Hunters.Desktop.Characters;
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

    /// <summary>
    /// A system's menu, like the web's: its name and security, then Focus system (or Clear focus on the
    /// focused one), and Navigate to: the destination for every tracked character, or for one character.
    /// </summary>
    private void OnSystemMenuRequested(object? sender, MapSystemMenuRequest request)
    {
        if (_viewModel is not { } vm || vm.Model is not { } model || vm.DescribeSystem(request.Index) is not { } system)
            return;

        var index = request.Index;
        var isFocused = vm.FocusedSystemId == model.Ids[index];
        var focus = new MenuItem
        {
            Header = isFocused ? "Clear focus" : "Focus system",
            Icon = isFocused ? null : MenuIcon("Hud.Icon.Target"),
        };
        focus.Click += (_, _) =>
        {
            if (isFocused)
                vm.ClearSystem();
            else
                vm.FocusSystem(index);
        };

        _menu?.Hide();
        _menu = new MenuFlyout { Items = { MenuHeader(system), new Separator(), focus, NavigateItem(vm, index) } };
        _menu.ShowAt(Map, showAtPointer: true);
    }

    /// <summary>"Navigate to": All (every tracked character), then each usable character, online first, with an online dot.</summary>
    private static MenuItem NavigateItem(MapWidgetViewModel vm, int index)
    {
        var (tracked, usable) = vm.NavigateChoices();
        var busy = vm.IsNavigating;
        var all = new MenuItem
        {
            Header = tracked.Count > 0 ? $"All ({tracked.Count})" : "All",
            Icon = MenuIcon("Hud.Icon.Flag"),
            IsEnabled = tracked.Count > 0 && !busy,
        };
        ToolTip.SetTip(all, tracked.Count == 0 ? "No tracked characters" : $"Every tracked character: {string.Join(", ", tracked.Select(c => c.Name))}");
        all.Click += (_, _) => vm.NavigateTo(index, tracked);

        var item = new MenuItem { Header = "Navigate to", Icon = MenuIcon("Hud.Icon.Flag"), Items = { all } };
        foreach (var character in usable)
            item.Items.Add(CharacterItem(vm, index, character, busy));
        return item;
    }

    private static MenuItem CharacterItem(MapWidgetViewModel vm, int index, DestinationCharacter character, bool busy)
    {
        var item = new MenuItem
        {
            Header = character.Name,
            Icon = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Resource<IBrush>(character.IsOnline ? "Hud.OkBrush" : "Hud.FgFaintBrush"),
            },
            IsEnabled = !busy,
        };
        ToolTip.SetTip(item, character.IsOnline ? "Online" : "Not online: the destination shows when it logs in, if at all");
        item.Click += (_, _) => vm.NavigateTo(index, [character]);
        return item;
    }

    /// <summary>The menu's first line: the system's name and security, not an item to choose.</summary>
    private static MenuItem MenuHeader(MapSystemInfo system) => new()
    {
        Header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = system.Name, FontWeight = FontWeight.SemiBold, FontFamily = Resource<FontFamily>("Hud.DisplayFont") ?? FontFamily.Default },
                new TextBlock { Text = system.Security, Foreground = system.SecurityBrush, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
            },
        },
        IsHitTestVisible = false,
        Focusable = false,
    };

    /// <summary>One of the HUD's stroke icons, for a menu item.</summary>
    private static Path MenuIcon(string geometry) => new()
    {
        Width = 14,
        Height = 14,
        Stretch = Stretch.Uniform,
        StrokeThickness = 2,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Data = Resource<Geometry>(geometry),
        Stroke = Resource<IBrush>("Hud.FgBrush"),
    };

    /// <summary>An app resource (the HUD's brushes, fonts and icons), for controls built in code.</summary>
    private static T? Resource<T>(string key) where T : class => Application.Current?.FindResource(key) as T;

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

using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// How the map is drawn: its layout, its colour filter and the filter's legend. A widget of its own,
/// so the map keeps its space (an overlay over the game stays clear) and the controls can sit wherever
/// suits: docked, or as an overlay of their own.
/// </summary>
public sealed class MapControlsWidgetViewModel : WidgetViewModelBase
{
    private readonly MapWidgetViewModel _map;

    public MapControlsWidgetViewModel(MapWidgetViewModel map, MapPreferences preferences)
    {
        _map = map;
        LayoutChoices = preferences.CreateLayoutChoices();
        FilterChoices = MapFilters.All
            .Select(f => new ChoiceViewModel(f.Label, new RelayCommand(() => map.FilterId = f.Id)) { IsSelected = f.Id == map.FilterId })
            .ToList();

        map.PropertyChanged += OnMapChanged;
    }

    public IReadOnlyList<ChoiceViewModel> LayoutChoices { get; }

    public IReadOnlyList<ChoiceViewModel> FilterChoices { get; }

    public IReadOnlyList<MapLegendItemViewModel> Legend =>
        _map.Filter?.Legend.Select(item => new MapLegendItemViewModel(item.Label, new SolidColorBrush(item.Colour))).ToList() ?? [];

    /// <summary>The map is loaded; until then the legend is empty.</summary>
    public bool IsLoaded => _map.IsLoaded;

    private void OnMapChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MapWidgetViewModel.Filter):
                OnPropertyChanged(nameof(Legend));
                break;
            case nameof(MapWidgetViewModel.FilterId):
                for (var i = 0; i < MapFilters.All.Count; i++)
                    FilterChoices[i].IsSelected = MapFilters.All[i].Id == _map.FilterId;
                break;
            case nameof(MapWidgetViewModel.IsLoaded):
                OnPropertyChanged(nameof(IsLoaded));
                break;
        }
    }
}

public sealed record MapLegendItemViewModel(string Label, IBrush Brush);

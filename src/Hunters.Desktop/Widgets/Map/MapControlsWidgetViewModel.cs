using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// How the map is drawn: its layout, its colour filter (with the minimum NPC kills for rising and
/// falling) and the filter's legend. A widget of its own, so the map keeps its space (an overlay over
/// the game stays clear) and the controls can sit wherever suits: docked, or as an overlay of their own.
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
        MinNpcChoices = MapFilters.MinNpcChoices
            .Select(min => new ChoiceViewModel(min == 0 ? "Any" : $"{min}+", new RelayCommand(() => map.MinNpc = min)) { IsSelected = min == map.MinNpc })
            .ToList();

        map.PropertyChanged += OnMapChanged;
    }

    public IReadOnlyList<ChoiceViewModel> LayoutChoices { get; }

    public IReadOnlyList<ChoiceViewModel> FilterChoices { get; }

    /// <summary>The colours offered: the activity ones only when the server has activity (or one is chosen).</summary>
    public IReadOnlyList<ChoiceViewModel> ShownFilterChoices =>
        FilterChoices.Where((_, i) => !MapFilters.All[i].NeedsActivity || _map.HasActivity || MapFilters.All[i].Id == _map.FilterId).ToList();

    public IReadOnlyList<ChoiceViewModel> MinNpcChoices { get; }

    /// <summary>NPC rising or falling is chosen: the minimum NPC kills is offered.</summary>
    public bool ShowsMinNpc => _map.FilterId is MapFilterId.NpcRising or MapFilterId.NpcFalling;

    /// <summary>Why the colour shows nothing yet (no activity, no hour before), shown instead of the legend.</summary>
    public string? Notice => _map.Filter?.Notice;

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
                OnPropertyChanged(nameof(Notice));
                break;
            case nameof(MapWidgetViewModel.FilterId):
                for (var i = 0; i < MapFilters.All.Count; i++)
                    FilterChoices[i].IsSelected = MapFilters.All[i].Id == _map.FilterId;
                OnPropertyChanged(nameof(ShowsMinNpc));
                OnPropertyChanged(nameof(ShownFilterChoices));
                break;
            case nameof(MapWidgetViewModel.MinNpc):
                for (var i = 0; i < MapFilters.MinNpcChoices.Count; i++)
                    MinNpcChoices[i].IsSelected = MapFilters.MinNpcChoices[i] == _map.MinNpc;
                break;
            case nameof(MapWidgetViewModel.HasActivity):
                OnPropertyChanged(nameof(ShownFilterChoices));
                break;
            case nameof(MapWidgetViewModel.IsLoaded):
                OnPropertyChanged(nameof(IsLoaded));
                break;
        }
    }
}

public sealed record MapLegendItemViewModel(string Label, IBrush Brush);

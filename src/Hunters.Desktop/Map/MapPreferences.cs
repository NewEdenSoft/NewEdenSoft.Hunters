using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>The user's map settings, shared by the map page and the Settings page and saved with the desktop settings.</summary>
public sealed class MapPreferences : ObservableObject
{
    public static readonly IReadOnlyList<(MapLayout Id, string Label)> Layouts =
    [
        (MapLayout.Flat, "2D map"),
        (MapLayout.TopDown, "Top-down"),
    ];

    private readonly ISettingsStore _settings;
    private MapLayout _layout;

    public MapPreferences(ISettingsStore settings)
    {
        _settings = settings;
        _layout = settings.Load().Map.Layout;
    }

    public MapLayout Layout
    {
        get => _layout;
        set
        {
            if (SetProperty(ref _layout, value))
                _settings.Update(s => s with { Map = s.Map with { Layout = value } });
        }
    }

    /// <summary>One toggle per layout, kept in step with <see cref="Layout"/>.</summary>
    public IReadOnlyList<ChoiceViewModel> CreateLayoutChoices()
    {
        var choices = Layouts.Select(l => new ChoiceViewModel(l.Label, new RelayCommand(() => Layout = l.Id)) { IsSelected = l.Id == Layout }).ToList();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Layout))
                return;
            for (var i = 0; i < Layouts.Count; i++)
                choices[i].IsSelected = Layouts[i].Id == Layout;
        };
        return choices;
    }
}

/// <summary>One option of a small set of choices; the selected one is highlighted.</summary>
public sealed partial class ChoiceViewModel(string label, IRelayCommand select) : ObservableObject
{
    public string Label => label;

    public IRelayCommand SelectCommand => select;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

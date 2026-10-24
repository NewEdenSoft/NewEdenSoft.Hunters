using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// Which hunting profiles the map shows, as a glow and badges: a toggle per profile, and what the glow
/// means. A widget of its own, like the web's Hunting panel (<c>HuntingPanel.tsx</c>); its state is the
/// map's (<see cref="MapWidgetViewModel.ShownProfiles"/>, saved in <see cref="MapPreferences.HuntingProfiles"/>).
/// </summary>
public sealed class HuntingWidgetViewModel : WidgetViewModelBase
{
    private readonly MapWidgetViewModel _map;

    public HuntingWidgetViewModel(MapWidgetViewModel map)
    {
        _map = map;
        map.PropertyChanged += OnMapChanged;
    }

    /// <summary>The server's hunting profiles, each a toggle; empty when it has none.</summary>
    public IReadOnlyList<HuntingChoiceViewModel> Choices =>
        [.. _map.HuntingProfiles.Select(p => new HuntingChoiceViewModel(
            p.Name, _map.StyleOf(p.Id), _map.ShownProfiles.Contains(p.Id), new RelayCommand(() => _map.ToggleHuntingProfile(p.Id))))];

    public bool HasChoices => _map.HuntingProfiles.Count > 0;

    /// <summary>What the glow means while some profile is shown, or how to start.</summary>
    public string Hint => _map.ShownProfiles.Count > 0
        ? "The glow is where a shown profile is likely now: usually seen there at this hour (UTC)."
        : "Show a profile to see where it is seen, and where it is likely now.";

    private void OnMapChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapWidgetViewModel.HuntingProfiles) or nameof(MapWidgetViewModel.ShownProfiles))
        {
            OnPropertyChanged(nameof(Choices));
            OnPropertyChanged(nameof(HasChoices));
            OnPropertyChanged(nameof(Hint));
        }
    }
}

/// <summary>A hunting profile's toggle: its mark and name, highlighted while shown.</summary>
public sealed class HuntingChoiceViewModel(string name, ProfileStyle style, bool isSelected, IRelayCommand toggle) : ObservableObject
{
    public string Name => name;

    public ProfileStyle Style => style;

    public bool IsSelected => isSelected;

    public IRelayCommand ToggleCommand => toggle;
}

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>The account's map settings, shared by the map's widgets and the Settings page and saved in its profile (<see cref="ProfileStore"/>).</summary>
public sealed class MapPreferences : ObservableObject
{
    public static readonly IReadOnlyList<(MapLayout Id, string Label)> Layouts =
    [
        (MapLayout.Flat, "2D map"),
        (MapLayout.TopDown, "Top-down"),
    ];

    private readonly ProfileStore _settings;
    private MapLayout _layout;
    private MapFilterId _filter;
    private int _minNpc;
    private IReadOnlyList<string> _huntingProfiles;
    private MapBehaviour _behaviour;
    private JumpRangeSetting _jumpRange;
    private NearbySetting _nearby;

    public MapPreferences(ProfileStore settings)
    {
        _settings = settings;
        var map = settings.Load().Map;
        _layout = map.Layout;
        // A file edited by hand may have a filter or a minimum not offered.
        _filter = Enum.IsDefined(map.Filter) ? map.Filter : MapFilterId.Security;
        _minNpc = MapFilters.MinNpcChoices.Contains(map.MinNpc) ? map.MinNpc : 0;
        _huntingProfiles = map.HuntingProfiles;
        // A file edited by hand (or from an older version) may lack it or have a choice not offered.
        var behaviour = map.Behaviour ?? new MapBehaviour();
        _behaviour = MapBehaviour.MinJumpChoices.Contains(behaviour.MinJumps) ? behaviour : behaviour with { MinJumps = new MapBehaviour().MinJumps };
        var jumpRange = map.JumpRange ?? new JumpRangeSetting();
        _jumpRange = jumpRange.Ly is >= JumpRanges.MinLy and <= JumpRanges.MaxLy ? jumpRange : jumpRange with { Ly = new JumpRangeSetting().Ly };
        _nearby = Map.Nearby.Validate(map.Nearby);
    }

    /// <summary>The map's colour filter (Map controls). An activity filter is kept while the server has no activity yet; the map says so.</summary>
    public MapFilterId Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value))
                _settings.Update(s => s with { Map = s.Map with { Filter = value } });
        }
    }

    /// <summary>For NPC rising and falling: the minimum NPC kills in the last hour (Map controls).</summary>
    public int MinNpc
    {
        get => _minNpc;
        set
        {
            if (SetProperty(ref _minNpc, value))
                _settings.Update(s => s with { Map = s.Map with { MinNpc = value } });
        }
    }

    /// <summary>What the Nearby widget looks for around the focused system: gate jumps, the jump range, the sort, and quiet systems.</summary>
    public NearbySetting Nearby
    {
        get => _nearby;
        set
        {
            if (SetProperty(ref _nearby, value))
                _settings.Update(s => s with { Map = s.Map with { Nearby = value } });
        }
    }

    /// <summary>Whether the jump range is drawn around the focused system, and how far it reaches (the Jump range widget). The range is kept within 0.5 to 20 ly.</summary>
    public JumpRangeSetting JumpRange
    {
        get => _jumpRange;
        set
        {
            value = value with { Ly = JumpRanges.Clamp(value.Ly) };
            if (SetProperty(ref _jumpRange, value))
                _settings.Update(s => s with { Map = s.Map with { JumpRange = value } });
        }
    }

    /// <summary>When the focused system changes by itself, and whether a double click focuses (Settings, "Map behaviour").</summary>
    public MapBehaviour Behaviour
    {
        get => _behaviour;
        set
        {
            if (SetProperty(ref _behaviour, value))
                _settings.Update(s => s with { Map = s.Map with { Behaviour = value } });
        }
    }

    /// <summary>The hunting profiles shown on the map, in the order they were turned on.</summary>
    public IReadOnlyList<string> HuntingProfiles
    {
        get => _huntingProfiles;
        private set
        {
            _huntingProfiles = value;
            OnPropertyChanged();
            _settings.Update(s => s with { Map = s.Map with { HuntingProfiles = value } });
        }
    }

    /// <summary>Shows a hunting profile on the map, or hides it.</summary>
    public void ToggleHuntingProfile(string id) =>
        HuntingProfiles = HuntingProfiles.Contains(id) ? [.. HuntingProfiles.Where(p => p != id)] : [.. HuntingProfiles, id];

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

    /// <summary>Shown when hovering the choice; null for none.</summary>
    public string? ToolTip { get; init; }
}

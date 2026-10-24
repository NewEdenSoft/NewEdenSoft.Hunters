using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly ThemeService _themes;
    private readonly ISettingsStore _settings;
    private readonly MapPreferences _map;

    public SettingsPageViewModel(ThemeService themes, ISettingsStore settings, MapPreferences map)
    {
        _themes = themes;
        _settings = settings;
        _map = map;

        ThemeGroups = themes.Catalog.Themes
            .GroupBy(t => t.Group)
            .Select(g => new ThemeGroupViewModel(
                g.Key.ToUpperInvariant(),
                g.Select(t => new ThemeOptionViewModel(t, SelectThemeCommand)).ToList()))
            .ToList();

        StartInTray = settings.Load().StartInTray;
        MapLayoutChoices = map.CreateLayoutChoices();
        MinJumpChoices = MapBehaviour.MinJumpChoices
            .Select(n => new ChoiceViewModel($"{n} jumps", new RelayCommand(() => map.Behaviour = map.Behaviour with { MinJumps = n })))
            .ToList();
        map.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MapPreferences.Behaviour))
                SyncBehaviour();
        };
        SyncBehaviour();

        themes.ThemeChanged += (_, theme) => Sync(theme);
        Sync(themes.Current);
    }

    public override string Title => "Settings";

    public IReadOnlyList<ThemeGroupViewModel> ThemeGroups { get; }

    public IReadOnlyList<ChoiceViewModel> MapLayoutChoices { get; }

    /// <summary>The minimum gate jumps of a move that focuses its new system.</summary>
    public IReadOnlyList<ChoiceViewModel> MinJumpChoices { get; }

    /// <summary>Double click a system to focus it.</summary>
    public bool DoubleClickFocus
    {
        get => _map.Behaviour.DoubleClickFocus;
        set => _map.Behaviour = _map.Behaviour with { DoubleClickFocus = value };
    }

    /// <summary>Focus the followed character's (or the main's) system when it comes into known space.</summary>
    public bool EnterKnownSpaceFocus
    {
        get => _map.Behaviour.EnterKnownSpace;
        set => _map.Behaviour = _map.Behaviour with { EnterKnownSpace = value };
    }

    /// <summary>Focus the followed character's (or the main's) system after a long move.</summary>
    public bool LongMoveFocus
    {
        get => _map.Behaviour.LongMove;
        set => _map.Behaviour = _map.Behaviour with { LongMove = value };
    }

    [ObservableProperty]
    public partial string CurrentThemeName { get; set; } = "";

    [ObservableProperty]
    public partial string CurrentThemeGroup { get; set; } = "";

    [ObservableProperty]
    public partial bool StartInTray { get; set; }

    partial void OnStartInTrayChanged(bool value) => _settings.Update(s => s with { StartInTray = value });

    [RelayCommand]
    private void SelectTheme(string themeId) => _themes.Apply(themeId);

    private void SyncBehaviour()
    {
        OnPropertyChanged(nameof(DoubleClickFocus));
        OnPropertyChanged(nameof(EnterKnownSpaceFocus));
        OnPropertyChanged(nameof(LongMoveFocus));
        for (var i = 0; i < MinJumpChoices.Count; i++)
            MinJumpChoices[i].IsSelected = MapBehaviour.MinJumpChoices[i] == _map.Behaviour.MinJumps;
    }

    private void Sync(ThemeDefinition theme)
    {
        CurrentThemeName = theme.Name;
        CurrentThemeGroup = theme.Group.ToUpperInvariant();
        foreach (var option in ThemeGroups.SelectMany(g => g.Themes))
            option.IsSelected = option.Id == theme.Id;
    }
}

public sealed record ThemeGroupViewModel(string Name, IReadOnlyList<ThemeOptionViewModel> Themes);

public sealed partial class ThemeOptionViewModel(ThemeDefinition theme, IRelayCommand<string> select) : ObservableObject
{
    public string Id => theme.Id;
    public string Name => theme.Name;
    public IBrush Swatch { get; } = new SolidColorBrush(ThemeService.ToColor(theme.Primary));
    public IRelayCommand<string> SelectCommand => select;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

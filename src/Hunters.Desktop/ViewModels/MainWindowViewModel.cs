using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ThemeService _themes;

    public MainWindowViewModel(ThemeService themes)
    {
        _themes = themes;

        ThemeGroups = new ObservableCollection<ThemeGroupViewModel>(
            themes.Catalog.Themes
                .GroupBy(t => t.Group)
                .Select(g => new ThemeGroupViewModel(
                    g.Key.ToUpperInvariant(),
                    g.Select(t => new ThemeOptionViewModel(t, SelectThemeCommand)).ToList())));

        SecurityScale = Enumerable.Range(0, 11)
            .Select(i => 1.0 - i / 10.0)
            .Select(s => new SecurityStepViewModel(s.ToString("0.0"), Brush(themes.Catalog.SecurityColor(s))))
            .Append(new SecurityStepViewModel("−1.0", Brush(themes.Catalog.SecurityNull)))
            .ToList();

        themes.ThemeChanged += (_, theme) => Sync(theme);
        Sync(themes.Current);
    }

    public ObservableCollection<ThemeGroupViewModel> ThemeGroups { get; }

    public IReadOnlyList<SecurityStepViewModel> SecurityScale { get; }

    [ObservableProperty]
    public partial string CurrentThemeName { get; set; } = "";

    [ObservableProperty]
    public partial string CurrentThemeGroup { get; set; } = "";

    [RelayCommand]
    private void SelectTheme(string themeId) => _themes.Apply(themeId);

    private void Sync(ThemeDefinition theme)
    {
        CurrentThemeName = theme.Name;
        CurrentThemeGroup = theme.Group.ToUpperInvariant();
        foreach (var option in ThemeGroups.SelectMany(g => g.Themes))
            option.IsSelected = option.Id == theme.Id;
    }

    private static IBrush Brush(Rgba color) => new SolidColorBrush(ThemeService.ToColor(color));
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

public sealed record SecurityStepViewModel(string Label, IBrush Color);

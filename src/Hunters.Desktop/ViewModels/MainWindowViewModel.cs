using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;
using NewEdenSoft.Hunters.Desktop.Widgets;

namespace NewEdenSoft.Hunters.Desktop.ViewModels;

/// <summary>Main window shell: navigation rail with view pages, widget pages and system pages.</summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(WidgetManager widgets, MapPageViewModel map, AccountPageViewModel account, SettingsPageViewModel settings)
    {
        ViewPages = [map];
        WidgetPages = widgets.Widgets.Select(w => new WidgetPageViewModel(w, widgets)).ToList();
        SystemPages = [account, settings];

        foreach (var page in WidgetPages)
            page.Widget.PropertyChanged += OnWidgetChanged;

        Navigate(WidgetPages.FirstOrDefault() ?? (PageViewModel)account);
    }

    public IReadOnlyList<PageViewModel> ViewPages { get; }

    public IReadOnlyList<WidgetPageViewModel> WidgetPages { get; }

    public IReadOnlyList<PageViewModel> SystemPages { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScrollingPage), nameof(FullBleedPage), nameof(IsFullBleed))]
    public partial PageViewModel? CurrentPage { get; private set; }

    /// <summary>The current page when it scrolls in the padded content area.</summary>
    public PageViewModel? ScrollingPage => CurrentPage is { IsFullBleed: false } ? CurrentPage : null;

    /// <summary>The current page when it fills the content area.</summary>
    public PageViewModel? FullBleedPage => CurrentPage is { IsFullBleed: true } ? CurrentPage : null;

    public bool IsFullBleed => CurrentPage?.IsFullBleed == true;

    /// <summary>Set by the main window as it is shown and hidden to the tray.</summary>
    [ObservableProperty]
    public partial bool IsWindowVisible { get; set; }

    [RelayCommand]
    public void Navigate(PageViewModel page)
    {
        if (CurrentPage is not null)
            CurrentPage.IsSelected = false;

        CurrentPage = page;
        page.IsSelected = true;
        UpdateDockedActivity();
    }

    /// <summary>Selects a widget's page, for example from the tray menu.</summary>
    public void NavigateToWidget(string widgetId)
    {
        if (WidgetPages.FirstOrDefault(p => p.Widget.Id == widgetId) is { } page)
            Navigate(page);
    }

    partial void OnIsWindowVisibleChanged(bool value) => UpdateDockedActivity();

    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WidgetState.IsFloating))
            UpdateDockedActivity();
    }

    /// <summary>Docked widgets are active only while their page is visible. Floating ones are handled by <see cref="WidgetManager"/>.</summary>
    private void UpdateDockedActivity()
    {
        foreach (var page in WidgetPages.Where(p => !p.Widget.IsFloating))
            page.Widget.Content.IsActive = IsWindowVisible && page.IsSelected;
    }
}

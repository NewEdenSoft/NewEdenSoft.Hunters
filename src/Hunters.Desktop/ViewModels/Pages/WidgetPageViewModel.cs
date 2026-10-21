using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.Widgets;

namespace NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

/// <summary>
/// A widget's page in the main window: the widget itself while docked, or its overlay settings
/// while it floats.
/// </summary>
public sealed partial class WidgetPageViewModel : PageViewModel
{
    private readonly WidgetManager _manager;

    public WidgetPageViewModel(WidgetState widget, WidgetManager manager)
    {
        Widget = widget;
        _manager = manager;
        widget.PropertyChanged += OnWidgetChanged;
    }

    public WidgetState Widget { get; }

    public override string Title => Widget.Title;

    public string Description => Widget.Definition.Description;

    public override string? Badge => Widget.IsFloating ? "OVERLAY" : null;

    /// <summary>Docked widgets that fill their page; floating, the page shows overlay settings, which scroll.</summary>
    public override bool IsFullBleed => Widget.Definition.FillsPage && !Widget.IsFloating;

    /// <summary>The widget content while docked; null while it floats, so only one view of it exists.</summary>
    public WidgetViewModelBase? DockedContent => Widget.IsFloating ? null : Widget.Content;

    public bool SupportsClickThrough => _manager.SupportsClickThrough;

    [RelayCommand]
    private void PopOut() => _manager.Float(Widget);

    [RelayCommand]
    private void Dock() => _manager.Dock(Widget);

    [RelayCommand]
    private void ResetPosition() => _manager.ResetPosition(Widget);

    private void OnWidgetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WidgetState.IsFloating))
        {
            OnPropertyChanged(nameof(Badge));
            OnPropertyChanged(nameof(DockedContent));
            OnPropertyChanged(nameof(IsFullBleed));
        }
    }
}

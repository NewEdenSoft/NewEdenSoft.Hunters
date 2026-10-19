using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace NewEdenSoft.Hunters.Desktop.Widgets;

public static class WidgetRegistrationExtensions
{
    /// <summary>
    /// Registers a widget: its content view model (one shared instance), its view, and its
    /// definition. The widget then appears in the main window's navigation, the tray menu and as
    /// an overlay when floated.
    /// </summary>
    public static IServiceCollection AddWidget<TViewModel, TView>(
        this IServiceCollection services,
        string id,
        string title,
        string description,
        double defaultWidth = 320,
        double defaultHeight = 240)
        where TViewModel : WidgetViewModelBase
        where TView : Control
    {
        services.AddSingleton<TViewModel>();
        services.AddView<TViewModel, TView>();
        services.AddSingleton(new WidgetDefinition(id, title, description, typeof(TViewModel), defaultWidth, defaultHeight));
        return services;
    }
}

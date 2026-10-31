using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace NewEdenSoft.Hunters.Desktop.Widgets;

public static class WidgetRegistrationExtensions
{
    /// <summary>
    /// Registers a widget: its content view model (one instance per session, shared by its page and its
    /// overlay), its view, and its definition. The widget then appears in the main window's navigation,
    /// the tray menu and as an overlay when floated.
    /// </summary>
    public static IServiceCollection AddWidget<TViewModel, TView>(
        this IServiceCollection services,
        string id,
        string title,
        string description,
        double defaultWidth = 320,
        double defaultHeight = 240,
        bool fillsPage = false)
        where TViewModel : WidgetViewModelBase
        where TView : Control
    {
        services.AddScoped<TViewModel>();
        services.AddView<TViewModel, TView>();
        services.AddSingleton(new WidgetDefinition(id, title, description, typeof(TViewModel), defaultWidth, defaultHeight, FillsPage: fillsPage));
        return services;
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Desktop.ViewModels;

namespace NewEdenSoft.Hunters.Desktop;

/// <summary>
/// Resolves the view for a view model from dependency injection. Register pairs with
/// <see cref="ViewRegistrationExtensions.AddView{TViewModel, TView}"/>; no reflection, so it is
/// safe to trim.
/// </summary>
public sealed class ViewLocator(IServiceProvider services, IEnumerable<ViewRegistration> registrations) : IDataTemplate
{
    private readonly Dictionary<Type, Type> _views = registrations.ToDictionary(r => r.ViewModelType, r => r.ViewType);

    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        return _views.TryGetValue(param.GetType(), out var viewType)
            ? (Control)services.GetRequiredService(viewType)
            : new TextBlock { Text = "No view registered for " + param.GetType().Name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}

public sealed record ViewRegistration(Type ViewModelType, Type ViewType);

public static class ViewRegistrationExtensions
{
    /// <summary>Registers a view model, its view, and the mapping the <see cref="ViewLocator"/> uses.</summary>
    public static IServiceCollection AddView<TViewModel, TView>(this IServiceCollection services)
        where TViewModel : ViewModelBase
        where TView : Control
    {
        services.AddTransient<TViewModel>();
        services.AddTransient<TView>();
        services.AddSingleton(new ViewRegistration(typeof(TViewModel), typeof(TView)));
        return services;
    }
}

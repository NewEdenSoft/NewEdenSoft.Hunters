using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.ViewModels;
using NewEdenSoft.Hunters.Desktop.Views;

namespace NewEdenSoft.Hunters.Desktop;

/// <summary>
/// Generic host for the desktop app: dependency injection, configuration, logging, and hosted
/// services for background work (ESI polling, log watching) that runs alongside the UI.
/// </summary>
public static class DesktopHost
{
    public static IHost Create(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.AddDesktopServices(builder.Configuration);

        return builder.Build();
    }

    public static IServiceCollection AddDesktopServices(this IServiceCollection services, IConfiguration configuration)
    {
        var settingsPath = configuration["Hunters:Desktop:SettingsPath"] ?? JsonFileSettingsStore.DefaultPath;
        services.AddSingleton<ISettingsStore>(sp =>
            new JsonFileSettingsStore(settingsPath, sp.GetRequiredService<ILogger<JsonFileSettingsStore>>()));

        services.AddSingleton(_ => ThemeCatalog.LoadEmbedded());
        services.AddSingleton<ThemeService>();

        services.AddSingleton<ViewLocator>();

        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();

        return services;
    }
}

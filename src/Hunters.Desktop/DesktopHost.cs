using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NewEdenSoft.Hunters.Client.Auth;
using NewEdenSoft.Hunters.Desktop.Images;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Platform;
using NewEdenSoft.Hunters.Desktop.Platform.Windows;
using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.Tray;
using NewEdenSoft.Hunters.Desktop.ViewModels;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;
using NewEdenSoft.Hunters.Desktop.Views;
using NewEdenSoft.Hunters.Desktop.Views.Pages;
using NewEdenSoft.Hunters.Desktop.Widgets;
using NewEdenSoft.Hunters.Desktop.Widgets.Placeholders;

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

        if (OperatingSystem.IsWindows())
            services.AddSingleton<IOverlayPlatform, WindowsOverlayPlatform>();
        else
            services.AddSingleton<IOverlayPlatform, NoOpOverlayPlatform>();

        services.AddSingleton<ViewLocator>();

        // Server connection
        services.AddSingleton<IBrowserLauncher, SystemBrowserLauncher>();
        var userAgent = $"Hunters.Desktop/{typeof(DesktopHost).Assembly.GetName().Version}";
        services.AddHttpClient<DesktopLoginClient>(http => http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent));
        // The server sends the map pack precompressed with Brotli or gzip.
        services.AddHttpClient(AccountPageViewModel.HttpClientName, http => http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.Brotli | DecompressionMethods.GZip });
        services.AddHttpClient(PortraitCache.HttpClientName, http => http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent));
        services.AddSingleton<PortraitCache>();

        // Shell
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<WidgetManager>();

        // View pages
        services.AddSingleton<MapPreferences>();
        services.AddSingleton<MapPageViewModel>();
        services.AddView<MapPageViewModel, MapPageView>();

        // System pages
        services.AddSingleton<AccountPageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddView<AccountPageViewModel, AccountPageView>();
        services.AddView<SettingsPageViewModel, SettingsPageView>();
        services.AddView<WidgetPageViewModel, WidgetPageView>();

        // Widgets, in navigation order
        services.AddWidget<NearbyActivityWidgetViewModel, PlaceholderWidgetView>(
            "nearby-activity", "Nearby activity", "Systems near your characters with recent kills and jumps.", 320, 260);
        services.AddWidget<FleetWidgetViewModel, PlaceholderWidgetView>(
            "fleet", "Fleet", "Your fleet session: members, locations and broadcast destinations.", 320, 220);

        return services;
    }
}

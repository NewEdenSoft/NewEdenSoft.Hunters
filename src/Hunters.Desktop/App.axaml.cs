using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Desktop.Sessions;
using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.Tray;
using NewEdenSoft.Hunters.Desktop.ViewModels;
using NewEdenSoft.Hunters.Desktop.Views;

namespace NewEdenSoft.Hunters.Desktop;

public partial class App : Application
{
    private readonly IServiceProvider? _services;

    /// <summary>Used by the XAML previewer, which runs without the host.</summary>
    public App()
    {
    }

    public App(IServiceProvider services) => _services = services;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        if (_services is null)
        {
            // Previewer: apply the default theme so DynamicResource colours resolve.
            new ThemeService(ThemeCatalog.LoadEmbedded(), new InMemorySettingsStore()).ApplySaved();
            return;
        }

        DataTemplates.Add(_services.GetRequiredService<ViewLocator>());
        _services.GetRequiredService<ThemeService>().ApplySaved();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_services is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The app lives in the tray: closing the main window hides it, Quit exits.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var window = _services.GetRequiredService<MainWindow>();
            window.DataContext = _services.GetRequiredService<MainWindowViewModel>();

            var sessions = _services.GetRequiredService<SessionHost>();
            var tray = _services.GetRequiredService<TrayIconService>();
            tray.Initialize(this, desktop);

            // Always opens on the login screen (docs/decisions.md, D20). The saved overlays come back with the
            // session; Start in the system tray applies to the first login after the app starts.
            desktop.MainWindow = window;
            var settings = _services.GetRequiredService<ISettingsStore>();
            EventHandler? firstLogin = null;
            firstLogin = (_, _) =>
            {
                if (sessions.Widgets is not { } widgets)
                    return;
                sessions.Changed -= firstLogin;
                if (settings.Load().StartInTray && widgets.Widgets.Any(w => w.IsFloating))
                    window.Hide();
            };
            sessions.Changed += firstLogin;

            desktop.Exit += (_, _) =>
            {
                // Already done when quitting from the tray; here for other ways out (Windows signing out).
                sessions.Quit();
                tray.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

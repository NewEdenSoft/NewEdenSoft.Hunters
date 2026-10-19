using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.Tray;
using NewEdenSoft.Hunters.Desktop.ViewModels;
using NewEdenSoft.Hunters.Desktop.Views;
using NewEdenSoft.Hunters.Desktop.Widgets;

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

            var widgets = _services.GetRequiredService<WidgetManager>();
            var tray = _services.GetRequiredService<TrayIconService>();
            tray.Initialize(this, desktop);
            widgets.RestoreOverlays();

            var startInTray = _services.GetRequiredService<ISettingsStore>().Load().StartInTray;
            if (!startInTray || !widgets.Widgets.Any(w => w.IsFloating))
                desktop.MainWindow = window;

            desktop.Exit += (_, _) =>
            {
                widgets.Shutdown();
                tray.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

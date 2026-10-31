using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using NewEdenSoft.Hunters.Desktop.Sessions;
using NewEdenSoft.Hunters.Desktop.Views;
using NewEdenSoft.Hunters.Desktop.Widgets;

namespace NewEdenSoft.Hunters.Desktop.Tray;

/// <summary>
/// System tray icon: opens the main window and quits the app; while signed in, also pops widgets out or
/// back in and hides all overlays. Signed in, the app keeps running in the tray when the main window is closed.
/// </summary>
public sealed class TrayIconService(SessionHost sessions, MainWindow mainWindow) : IDisposable
{
    private TrayIcon? _tray;
    private IClassicDesktopStyleApplicationLifetime? _lifetime;

    public void Initialize(Application app, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://NewEdenSoft.Hunters.Desktop/Assets/avalonia-logo.ico"))),
            ToolTipText = "Hunters",
            Menu = BuildMenu(),
        };
        _tray.Clicked += (_, _) => mainWindow.ShowAndActivate();
        sessions.Changed += OnSessionChanged;

        TrayIcon.SetIcons(app, [_tray]);
    }

    public void Dispose()
    {
        sessions.Changed -= OnSessionChanged;
        _tray?.Dispose();
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_tray is not null)
            _tray.Menu = BuildMenu();
    }

    /// <summary>Open and Quit; the overlay items only while signed in.</summary>
    private NativeMenu BuildMenu()
    {
        var menu = new NativeMenu();

        var open = new NativeMenuItem("Open Hunters");
        open.Click += (_, _) => mainWindow.ShowAndActivate();
        menu.Add(open);
        menu.Add(new NativeMenuItemSeparator());

        if (sessions.Widgets is { } widgets)
        {
            AddOverlayItems(menu, widgets);
            menu.Add(new NativeMenuItemSeparator());
        }

        var quit = new NativeMenuItem("Quit Hunters");
        quit.Click += (_, _) =>
        {
            // Saves the layout and closes the overlays before the windows close, so they are not docked.
            sessions.Quit();
            _lifetime?.Shutdown();
        };
        menu.Add(quit);

        return menu;
    }

    private static void AddOverlayItems(NativeMenu menu, WidgetManager widgets)
    {
        var overlays = new NativeMenu();
        foreach (var widget in widgets.Widgets)
        {
            var item = new NativeMenuItem(widget.Title)
            {
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = widget.IsFloating,
            };
            item.Click += (_, _) => widget.IsFloating = !widget.IsFloating;
            widget.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WidgetState.IsFloating))
                    item.IsChecked = widget.IsFloating;
            };
            overlays.Add(item);
        }
        menu.Add(new NativeMenuItem("Overlays") { Menu = overlays });

        var hide = new NativeMenuItem("Hide all overlays")
        {
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = widgets.OverlaysHidden,
        };
        hide.Click += (_, _) => widgets.OverlaysHidden = !widgets.OverlaysHidden;
        widgets.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WidgetManager.OverlaysHidden))
                hide.IsChecked = widgets.OverlaysHidden;
        };
        menu.Add(hide);
    }
}

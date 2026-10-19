using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using NewEdenSoft.Hunters.Desktop.Views;
using NewEdenSoft.Hunters.Desktop.Widgets;

namespace NewEdenSoft.Hunters.Desktop.Tray;

/// <summary>
/// System tray icon: opens the main window, pops widgets out or back in, hides all overlays and
/// quits the app. The app keeps running in the tray when the main window is closed.
/// </summary>
public sealed class TrayIconService(WidgetManager widgets, MainWindow mainWindow) : IDisposable
{
    private TrayIcon? _tray;

    public void Initialize(Application app, IClassicDesktopStyleApplicationLifetime lifetime)
    {
        var menu = new NativeMenu();

        var open = new NativeMenuItem("Open Hunters");
        open.Click += (_, _) => mainWindow.ShowAndActivate();
        menu.Add(open);
        menu.Add(new NativeMenuItemSeparator());

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
        menu.Add(new NativeMenuItemSeparator());

        var quit = new NativeMenuItem("Quit Hunters");
        quit.Click += (_, _) => lifetime.Shutdown();
        menu.Add(quit);

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://NewEdenSoft.Hunters.Desktop/Assets/avalonia-logo.ico"))),
            ToolTipText = "Hunters",
            Menu = menu,
        };
        _tray.Clicked += (_, _) => mainWindow.ShowAndActivate();

        TrayIcon.SetIcons(app, [_tray]);
    }

    public void Dispose() => _tray?.Dispose();
}

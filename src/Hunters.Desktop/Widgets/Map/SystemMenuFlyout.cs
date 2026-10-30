using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using NewEdenSoft.Hunters.Desktop.Characters;
using Path = Avalonia.Controls.Shapes.Path;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>The flyout for a <see cref="SystemMenu"/>, the same on the map and on a Nearby card.</summary>
public static class SystemMenuFlyout
{
    public static MenuFlyout Create(SystemMenu menu)
    {
        var focus = new MenuItem
        {
            Header = menu.FocusText,
            Icon = menu.IsFocused ? null : MenuIcon("Hud.Icon.Target"),
        };
        focus.Click += (_, _) =>
        {
            if (menu.IsFocused)
                menu.ClearFocus();
            else
                menu.Focus();
        };

        return new MenuFlyout { Items = { Header(menu.System), new Separator(), focus, NavigateItem(menu) } };
    }

    /// <summary>"Navigate to": All (every tracked character), then each usable character, online first, with an online dot.</summary>
    private static MenuItem NavigateItem(SystemMenu menu)
    {
        var all = new MenuItem { Header = menu.AllText, Icon = MenuIcon("Hud.Icon.Flag"), IsEnabled = menu.CanNavigateAll };
        ToolTip.SetTip(all, menu.AllTip);
        all.Click += (_, _) => menu.Navigate(menu.Tracked);

        var item = new MenuItem { Header = "Navigate to", Icon = MenuIcon("Hud.Icon.Flag"), Items = { all } };
        foreach (var character in menu.Usable)
            item.Items.Add(CharacterItem(character, menu.IsBusy, () => menu.Navigate([character])));
        return item;
    }

    /// <summary>A usable character, with a dot for online, to set the destination for just that one (here and the System widget's ▾).</summary>
    public static MenuItem CharacterItem(DestinationCharacter character, bool isBusy, Action choose)
    {
        var item = new MenuItem
        {
            Header = character.Name,
            Icon = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Resource<IBrush>(character.IsOnline ? "Hud.OkBrush" : "Hud.FgFaintBrush"),
            },
            IsEnabled = !isBusy,
        };
        ToolTip.SetTip(item, SystemMenu.CharacterTip(character));
        item.Click += (_, _) => choose();
        return item;
    }

    /// <summary>The menu's first line: the system's name and security, not an item to choose.</summary>
    private static MenuItem Header(MapSystemInfo system) => new()
    {
        // One TextBlock of runs, so the name and the security share a baseline across the two fonts.
        Header = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            Inlines = new InlineCollection
            {
                new Run(system.Name) { FontFamily = Resource<FontFamily>("Hud.DisplayFont") ?? FontFamily.Default },
                new Run("  "),
                new Run(system.Security) { Foreground = system.SecurityBrush, FontFamily = Resource<FontFamily>("Hud.MonoFont") ?? FontFamily.Default },
            },
        },
        IsHitTestVisible = false,
        Focusable = false,
    };

    /// <summary>One of the HUD's stroke icons, for a menu item.</summary>
    private static Path MenuIcon(string geometry) => new()
    {
        Width = 14,
        Height = 14,
        Stretch = Stretch.Uniform,
        StrokeThickness = 2,
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Data = Resource<Geometry>(geometry),
        Stroke = Resource<IBrush>("Hud.FgBrush"),
    };

    /// <summary>An app resource (the HUD's brushes, fonts and icons), for controls built in code.</summary>
    private static T? Resource<T>(string key) where T : class => Application.Current?.FindResource(key) as T;
}

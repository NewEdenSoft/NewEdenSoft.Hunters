using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

public partial class SystemWidgetView : UserControl
{
    public SystemWidgetView() => InitializeComponent();

    /// <summary>zKillboard or Dotlan, in the browser.</summary>
    private async void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is Uri url && TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(url);
    }

    /// <summary>Destination's ▾: the usable characters, online first, then the main, to set it for one.</summary>
    private void OnChooseCharacter(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SystemWidgetViewModel vm)
            return;

        var characters = vm.UsableCharacters();
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        if (characters.Count == 0)
            menu.Items.Add(new MenuItem { Header = "No characters to use", IsEnabled = false });
        foreach (var character in characters)
            menu.Items.Add(SystemMenuFlyout.CharacterItem(character, isBusy: false, () => vm.SetDestinationFor([character])));
        menu.ShowAt(ChooseCharacter);
    }
}

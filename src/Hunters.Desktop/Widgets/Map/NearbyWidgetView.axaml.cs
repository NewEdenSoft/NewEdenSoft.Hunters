using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

public partial class NearbyWidgetView : UserControl
{
    public NearbyWidgetView() => InitializeComponent();

    /// <summary>A click on a card selects its system and takes the map there; its quick controls don't.</summary>
    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Control)?.FindAncestorOfType<Button>(includeSelf: true) is null)
            Select(sender);
    }

    /// <summary>Enter or Space on a focused card does the same as a click.</summary>
    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || e.Source != sender)
            return;
        Select(sender);
        e.Handled = true;
    }

    private void Select(object? sender)
    {
        if (DataContext is NearbyWidgetViewModel widget && (sender as Control)?.DataContext is NearbyCardViewModel card)
            widget.SelectCard(card);
    }
}

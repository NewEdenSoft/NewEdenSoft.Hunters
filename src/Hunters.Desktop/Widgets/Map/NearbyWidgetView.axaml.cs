using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    /// <summary>The pointer or the keyboard's focus came onto a card: the map shows the gate route there.</summary>
    private void OnCardEntered(object? sender, RoutedEventArgs e) => Hover(sender, true);

    private void OnCardExited(object? sender, RoutedEventArgs e) => Hover(sender, false);

    private void Hover(object? sender, bool on)
    {
        if (DataContext is NearbyWidgetViewModel widget && sender is Control { DataContext: NearbyCardViewModel card } control)
            widget.HoverCard(card, on || control.IsPointerOver || control.IsKeyboardFocusWithin);
    }

    private void Select(object? sender)
    {
        if (DataContext is NearbyWidgetViewModel widget && (sender as Control)?.DataContext is NearbyCardViewModel card)
            widget.SelectCard(card);
    }
}

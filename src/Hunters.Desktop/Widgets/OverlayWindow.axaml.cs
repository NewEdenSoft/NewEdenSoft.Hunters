using Avalonia.Controls;
using Avalonia.Input;

namespace NewEdenSoft.Hunters.Desktop.Widgets;

/// <summary>
/// Borderless, transparent, always-on-top window hosting one widget over the game. Created and
/// owned by <see cref="WidgetManager"/>; the data context is the widget's <see cref="WidgetState"/>.
/// </summary>
public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();

        Header.PointerPressed += (_, e) =>
        {
            if (CanMove && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };

        Grip.PointerPressed += (_, e) =>
        {
            if (CanMove && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginResizeDrag(WindowEdge.SouthEast, e);
                e.Handled = true;
            }
        };
    }

    private bool CanMove => DataContext is WidgetState { CanMove: true };
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NewEdenSoft.Hunters.Desktop.ViewModels;

namespace NewEdenSoft.Hunters.Desktop.Views;

/// <summary>
/// The main window. Signed in, closing it hides it to the system tray; the app keeps running with its
/// overlays until the user quits from the tray menu. Signed out, closing it quits the app.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Shows the window, restoring it if minimised, and brings it to the front.</summary>
    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic)
        {
            e.Cancel = true;
            if (DataContext is MainWindowViewModel { QuitsOnClose: true }
                && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                // After this close is handled; shutting down closes the window again, programmatically.
                Dispatcher.UIThread.Post(() => lifetime.Shutdown());
            else
                Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (DataContext is MainWindowViewModel vm
            && (change.Property == IsVisibleProperty || change.Property == WindowStateProperty))
        {
            vm.IsWindowVisible = IsVisible && WindowState != WindowState.Minimized;
        }
    }
}

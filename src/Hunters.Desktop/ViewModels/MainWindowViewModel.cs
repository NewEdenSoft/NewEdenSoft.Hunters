using CommunityToolkit.Mvvm.ComponentModel;
using NewEdenSoft.Hunters.Desktop.Sessions;

namespace NewEdenSoft.Hunters.Desktop.ViewModels;

/// <summary>
/// The main window: only the login screen while signed out; the shell (navigation rail, widget pages,
/// Account, Settings) while signed in (docs/decisions.md, D19).
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(LoginViewModel login, SessionHost sessions)
    {
        Login = login;
        sessions.Changed += (_, _) => Shell = sessions.Shell;
        Shell = sessions.Shell;
    }

    public LoginViewModel Login { get; }

    /// <summary>The signed-in session's shell; null while signed out.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(QuitsOnClose))]
    public partial ShellViewModel? Shell { get; private set; }

    public bool IsSignedIn => Shell is not null;

    /// <summary>
    /// Closing the window quits the app while signed out: there are no overlays or background work to keep
    /// in the tray. Signed in, it hides to the tray.
    /// </summary>
    public bool QuitsOnClose => Shell is null;

    /// <summary>Set by the main window as it is shown and hidden to the tray.</summary>
    [ObservableProperty]
    public partial bool IsWindowVisible { get; set; }

    partial void OnIsWindowVisibleChanged(bool value)
    {
        if (Shell is { } shell)
            shell.IsWindowVisible = value;
    }

    partial void OnShellChanged(ShellViewModel? value)
    {
        if (value is not null)
            value.IsWindowVisible = IsWindowVisible;
    }
}

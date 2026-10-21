using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Client;
using NewEdenSoft.Hunters.Client.Auth;
using NewEdenSoft.Hunters.Contracts.Auth;

namespace NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

/// <summary>
/// Log in to a Hunters server with EVE Online. The session is kept in memory for now; saved
/// servers, credential storage and character management follow.
/// </summary>
public sealed partial class AccountPageViewModel(DesktopLoginClient logins) : PageViewModel
{
    private CancellationTokenSource? _login;

    public override string Title => "Account";

    public override string? Badge => Session is null ? null : "ONLINE";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand))]
    public partial string ServerUrl { get; set; } = "";

    [ObservableProperty]
    public partial string AccessKey { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand))]
    public partial bool IsLoggingIn { get; private set; }

    public bool IsIdle => !IsLoggingIn;

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn), nameof(Badge))]
    public partial LoggedInSession? Session { get; private set; }

    public bool IsLoggedIn => Session is not null;

    [RelayCommand(CanExecute = nameof(CanLogIn))]
    private async Task LogInAsync()
    {
        ErrorMessage = null;
        if (!ServerAddress.TryParse(ServerUrl, out var server))
        {
            ErrorMessage = "Enter a server address, for example hunters.example.com.";
            return;
        }

        _login = new CancellationTokenSource();
        IsLoggingIn = true;
        try
        {
            var result = await logins.LoginAsync(server, AccessKey, Environment.MachineName, cancellationToken: _login.Token);
            var character = result.Account.Characters.FirstOrDefault(c => c.Id == result.CharacterId);
            Session = new LoggedInSession(server, result.SessionToken, result.CharacterId, character?.Name ?? $"Character {result.CharacterId}", result.IsNewAccount);
            AccessKey = "";
        }
        catch (OperationCanceledException) when (_login.IsCancellationRequested)
        {
            // Cancelled by the user.
        }
        catch (HuntersLoginException e)
        {
            ErrorMessage = Describe(e);
        }
        finally
        {
            IsLoggingIn = false;
            _login.Dispose();
            _login = null;
        }
    }

    private bool CanLogIn() => !IsLoggingIn && !string.IsNullOrWhiteSpace(ServerUrl);

    [RelayCommand]
    private void CancelLogIn() => _login?.Cancel();

    /// <summary>Forgets the session on this device. Server-side revocation comes with session management.</summary>
    [RelayCommand]
    private void LogOut() => Session = null;

    private static string Describe(HuntersLoginException e) => e.Code switch
    {
        AuthErrorCodes.AccessKeyRequired => "This server needs an access key to create an account. Enter one and log in again.",
        AuthErrorCodes.AccessKeyInvalid => "That access key is not valid.",
        AuthErrorCodes.LoginCancelled => "The login was cancelled in the browser.",
        _ => e.Message,
    };
}

public sealed record LoggedInSession(Uri Server, string SessionToken, long CharacterId, string CharacterName, bool IsNewAccount)
{
    public string ServerHost => Server.Authority;

    public string Greeting => IsNewAccount ? "Account created" : "Logged in";
}

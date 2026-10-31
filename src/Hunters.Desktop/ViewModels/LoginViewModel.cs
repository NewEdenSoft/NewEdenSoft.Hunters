using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Client;
using NewEdenSoft.Hunters.Client.Auth;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Desktop.Sessions;
using NewEdenSoft.Hunters.Desktop.Settings;

namespace NewEdenSoft.Hunters.Desktop.ViewModels;

/// <summary>
/// The login screen, all the main window shows while signed out: log in to a Hunters server with EVE
/// Online. The session is kept in memory only and never saved (docs/decisions.md, D20), so every start of
/// the app begins here. Server addresses and their access keys can be saved (<see cref="Settings.SavedServers"/>);
/// the last saved is filled in at start.
/// </summary>
public sealed partial class LoginViewModel(
    DesktopLoginClient logins,
    IHttpClientFactory httpClients,
    SavedServers servers,
    SessionHost sessions) : ViewModelBase
{
    public const string HttpClientName = "Hunters";

    private CancellationTokenSource? _browserLogin;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand), nameof(SaveServerCommand))]
    public partial string ServerUrl { get; set; } = servers.All.FirstOrDefault()?.Label ?? "";

    [ObservableProperty]
    public partial string AccessKey { get; set; } = servers.All.FirstOrDefault()?.AccessKey ?? "";

    /// <summary>The servers saved on this computer, last saved first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedServers))]
    public partial IReadOnlyList<SavedServer> SavedServers { get; private set; } = servers.All;

    public bool HasSavedServers => SavedServers.Count > 0;

    /// <summary>"Saved hunters.example.com with its access key." after Save; cleared when another server is picked.</summary>
    [ObservableProperty]
    public partial string? SavedText { get; private set; }

    /// <summary>True while the browser is open for the login; the login can be cancelled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand))]
    public partial bool IsWaitingForBrowser { get; private set; }

    public bool IsIdle => !IsWaitingForBrowser;

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>Saves the address, and the access key when one is entered, on this computer.</summary>
    [RelayCommand(CanExecute = nameof(CanLogIn))]
    private void SaveServer()
    {
        if (!ServerAddress.TryParse(ServerUrl, out var server))
        {
            ErrorMessage = "Enter a server address, for example hunters.example.com.";
            return;
        }

        var saved = servers.Save(server, AccessKey);
        SavedServers = servers.All;
        ErrorMessage = null;
        SavedText = $"Saved {saved.Label}{(saved.HasAccessKey ? " with its access key" : "")}.";
    }

    /// <summary>Fills in a saved server's address and access key.</summary>
    [RelayCommand]
    private void PickServer(SavedServer server)
    {
        ServerUrl = server.Label;
        AccessKey = server.AccessKey ?? "";
        SavedText = null;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void ForgetServer(SavedServer server)
    {
        servers.Remove(server);
        SavedServers = servers.All;
        SavedText = null;
    }

    /// <summary>Logs in in the browser; on success the signed-in app replaces the login screen.</summary>
    [RelayCommand(CanExecute = nameof(CanLogIn))]
    private async Task LogInAsync()
    {
        if (!ServerAddress.TryParse(ServerUrl, out var server))
        {
            ErrorMessage = "Enter a server address, for example hunters.example.com.";
            return;
        }

        using var cancel = new CancellationTokenSource();
        _browserLogin = cancel;
        IsWaitingForBrowser = true;
        ErrorMessage = null;
        try
        {
            var result = await logins.LoginAsync(server, AccessKey, Environment.MachineName, cancellationToken: cancel.Token);
            AccessKey = "";
            SavedText = null;
            sessions.Start(new HuntersSession(httpClients.CreateClient(HttpClientName), server, result));
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user.
        }
        catch (HuntersLoginException e)
        {
            ErrorMessage = Describe(e.Code, e.Message);
        }
        catch (HuntersApiException e)
        {
            ErrorMessage = Describe(e.Code, e.Message);
        }
        finally
        {
            IsWaitingForBrowser = false;
            _browserLogin = null;
        }
    }

    private bool CanLogIn() => IsIdle && !string.IsNullOrWhiteSpace(ServerUrl);

    [RelayCommand]
    private void CancelLogIn() => _browserLogin?.Cancel();

    /// <summary>A login or account error as the user should read it.</summary>
    internal static string Describe(string? code, string message) => code switch
    {
        AuthErrorCodes.AccessKeyRequired => "This server needs an access key to create an account. Enter one and log in again.",
        AuthErrorCodes.AccessKeyInvalid => "That access key is not valid.",
        AuthErrorCodes.LoginCancelled => "The login was cancelled in the browser.",
        _ => message,
    };
}

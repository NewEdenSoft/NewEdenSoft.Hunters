using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Client;
using NewEdenSoft.Hunters.Client.Auth;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Images;

namespace NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

/// <summary>
/// Log in to a Hunters server with EVE Online and manage the account's characters. The session
/// is kept in memory only and never saved (docs/decisions.md, D20).
/// </summary>
public sealed partial class AccountPageViewModel(
    DesktopLoginClient logins,
    IHttpClientFactory httpClients,
    PortraitCache portraits,
    CharacterPreferences preferences,
    CharacterTracking tracking,
    EveNames names) : PageViewModel
{
    public const string HttpClientName = "Hunters";

    private CancellationTokenSource? _browserLogin;

    public override string Title => "Account";

    public override string? Badge => Session is null ? null : NeedsAttention ? "LOG IN" : "ONLINE";

    // Logged out

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand))]
    public partial string ServerUrl { get; set; } = "";

    [ObservableProperty]
    public partial string AccessKey { get; set; } = "";

    // State

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoggedIn), nameof(IsLoggedOut), nameof(Badge), nameof(ServerHost))]
    public partial HuntersSession? Session { get; private set; }

    public bool IsLoggedIn => Session is not null;

    public bool IsLoggedOut => Session is null;

    public string? ServerHost => Session?.Server.Authority;

    /// <summary>Set while a request runs; disables the actions.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(LogInCommand))]
    public partial string? BusyText { get; private set; }

    public bool IsBusy => BusyText is not null;

    public bool IsIdle => BusyText is null;

    /// <summary>True while the browser is open for a login; the login can be cancelled.</summary>
    [ObservableProperty]
    public partial bool IsWaitingForBrowser { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    // Logged in

    public ObservableCollection<CharacterItemViewModel> Characters { get; } = [];

    /// <summary>A character needs a new login: its refresh token expired or was revoked, or scopes are missing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge))]
    public partial bool NeedsAttention { get; private set; }

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    [RelayCommand(CanExecute = nameof(CanLogIn))]
    private async Task LogInAsync()
    {
        if (!ServerAddress.TryParse(ServerUrl, out var server))
        {
            ErrorMessage = "Enter a server address, for example hunters.example.com.";
            return;
        }

        await RunBrowserLoginAsync(async ct =>
        {
            var result = await logins.LoginAsync(server, AccessKey, Environment.MachineName, cancellationToken: ct);
            AccessKey = "";
            SetSession(new HuntersSession(httpClients.CreateClient(HttpClientName), server, result));
        });
    }

    private bool CanLogIn() => IsIdle && !string.IsNullOrWhiteSpace(ServerUrl);

    [RelayCommand]
    private void CancelLogIn() => _browserLogin?.Cancel();

    [RelayCommand]
    private Task AddCharacterAsync() => RunBrowserLoginAsync(async ct =>
    {
        if (Session is { } session)
            await session.AddCharacterAsync(logins, Environment.MachineName, ct);
    });

    internal Task ReauthAsync(CharacterItemViewModel character) => RunBrowserLoginAsync(async ct =>
    {
        if (Session is not { } session)
            return;

        var result = await session.AddCharacterAsync(logins, Environment.MachineName, ct);
        if (result.CharacterId != character.Id)
        {
            var picked = result.Account.Characters.FirstOrDefault(c => c.Id == result.CharacterId)?.Name ?? "another character";
            ErrorMessage = $"You logged in with {picked}, not {character.Name}. Choose {character.Name} on the EVE login page.";
        }
    });

    internal Task MakeMainAsync(CharacterItemViewModel character) =>
        RunAsync("Updating…", ct => Session!.SetMainCharacterAsync(character.Id, ct));

    internal Task RemoveAsync(CharacterItemViewModel character) =>
        RunAsync($"Removing {character.Name}…", ct => Session!.RemoveCharacterAsync(character.Id, ct));

    [RelayCommand]
    private Task RefreshAsync() => RunAsync("Refreshing…", ct => Session!.RefreshAsync(ct));

    [RelayCommand]
    private void ConfirmDelete() => IsConfirmingDelete = true;

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    [RelayCommand]
    private Task DeleteAccountAsync() => RunAsync("Deleting account…", ct => Session!.DeleteAccountAsync(ct));

    [RelayCommand]
    private Task LogOutAsync() => RunAsync("Logging out…", ct => Session!.LogOutAsync(ct));

    /// <summary>Runs a login in the browser; the user can cancel while it waits.</summary>
    private async Task RunBrowserLoginAsync(Func<CancellationToken, Task> login)
    {
        if (IsBusy)
            return;

        using var cancel = new CancellationTokenSource();
        _browserLogin = cancel;
        IsWaitingForBrowser = true;
        try
        {
            await RunAsync("Waiting for the browser…", _ => login(cancel.Token));
        }
        finally
        {
            IsWaitingForBrowser = false;
            _browserLogin = null;
        }
    }

    private async Task RunAsync(string busyText, Func<CancellationToken, Task> action)
    {
        if (IsBusy)
            return;

        ErrorMessage = null;
        BusyText = busyText;
        try
        {
            await action(CancellationToken.None);
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
            BusyText = null;
            IsConfirmingDelete = false;
        }
    }

    private void SetSession(HuntersSession? session)
    {
        if (Session is { } previous)
        {
            previous.AccountChanged -= OnAccountChanged;
            previous.Ended -= OnSessionEnded;
        }

        Session = session;
        if (session is not null)
        {
            session.AccountChanged += OnAccountChanged;
            session.Ended += OnSessionEnded;
        }

        SyncCharacters();

        tracking.Changed -= OnTrackingChanged;
        tracking.Changed += OnTrackingChanged;
        tracking.SetSession(session);
    }

    // Raised on the polling thread.
    private void OnTrackingChanged(object? sender, TrackedCharacterChangedEventArgs e) => Dispatcher.UIThread.Post(() =>
        Characters.FirstOrDefault(c => c.Id == e.CharacterId)?.ShowTracking(e.Current));

    // Session events can come from background work (access token requests), so they are marshalled to the UI thread.
    private void OnAccountChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(SyncCharacters);

    private void OnSessionEnded(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (ReferenceEquals(sender, Session))
            SetSession(null);
    });

    private void SyncCharacters()
    {
        Characters.Clear();
        if (Session is { } session)
        {
            foreach (var character in session.Account.Characters.OrderByDescending(c => c.IsMain).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var item = new CharacterItemViewModel(character, this, portraits, preferences, names);
                item.ShowTracking(tracking.Get(character.Id));
                Characters.Add(item);
            }
        }

        NeedsAttention = Characters.Any(c => c.NeedsReauth);
    }

    private static string Describe(string? code, string message) => code switch
    {
        AuthErrorCodes.AccessKeyRequired => "This server needs an access key to create an account. Enter one and log in again.",
        AuthErrorCodes.AccessKeyInvalid => "That access key is not valid.",
        AuthErrorCodes.LoginCancelled => "The login was cancelled in the browser.",
        _ => message,
    };
}

/// <summary>A character on the signed-in account.</summary>
public sealed partial class CharacterItemViewModel : ObservableObject
{
    private readonly CharacterInfo _character;
    private readonly AccountPageViewModel _page;
    private readonly CharacterPreferences _preferences;
    private readonly EveNames _names;
    private int _trackingVersion;

    public CharacterItemViewModel(CharacterInfo character, AccountPageViewModel page, PortraitCache portraits, CharacterPreferences preferences, EveNames names)
    {
        _character = character;
        _page = page;
        _preferences = preferences;
        _names = names;
        Marker = preferences.GetMarker(character.Id, character.Name);
        IsTracked = preferences.IsTracked(character.Id);
        _ = LoadPortraitAsync(portraits);
    }

    public long Id => _character.Id;

    public string Name => _character.Name;

    public bool IsMain => _character.IsMain;

    public bool CanMakeMain => !_character.IsMain;

    /// <summary>The server cannot get tokens for this character until the user logs in with it again.</summary>
    public bool NeedsReauth => _character.Status != CharacterStatus.Active;

    public string? StatusText => _character.Status switch
    {
        CharacterStatus.NeedsReauth => "Access expired or was revoked. Log in with this character again.",
        CharacterStatus.MissingScopes => "The server needs more permissions. Log in with this character again.",
        _ => null,
    };

    /// <summary>Null until downloaded, or when the image server cannot be reached.</summary>
    [ObservableProperty]
    public partial Bitmap? Portrait { get; private set; }

    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    /// <summary>The user wants this character's location polled. Saved for this computer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackingText), nameof(IsOnline), nameof(HasTrackingProblem))]
    public partial bool IsTracked { get; set; }

    /// <summary>What the tracker knows; null while not tracked or not started.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TrackingText), nameof(IsOnline), nameof(HasTrackingProblem), nameof(TrackingError))]
    public partial TrackedCharacter? Tracked { get; set; }

    public bool IsOnline => IsTracked && Tracked?.IsOnline == true;

    /// <summary>Polling fails (ESI, the server or a permission); the last known state is still shown.</summary>
    public bool HasTrackingProblem => IsTracked && Tracked?.Status == TrackingStatus.Failing;

    public string? TrackingError => Tracked?.Error;

    /// <summary>For example "Online · Jita · Docked · Ishtar", or "Offline · Amarr · Capsule".</summary>
    public string TrackingText
    {
        get
        {
            if (!IsTracked)
                return "Not tracked";
            if (Tracked is not { Location: { } location } state)
                return Tracked?.Status == TrackingStatus.Failing ? "Location unavailable, retrying" : "Locating…";

            var parts = new List<string> { state.IsOnline == true ? "Online" : "Offline" };
            parts.Add(_names.Get(location.SolarSystemId) ?? "Unknown system");
            if (location.IsDocked)
                parts.Add(location.StationId is { } station && _names.Get(station) is { } stationName ? $"Docked at {stationName}" : "Docked");
            if (state.Ship is { } ship)
            {
                var type = _names.Get(ship.TypeId);
                parts.Add(type is null ? ship.Name : ship.Name == type || string.IsNullOrWhiteSpace(ship.Name) ? type : $"{type} “{ship.Name}”");
            }

            if (state.Status == TrackingStatus.Failing)
                parts.Add("retrying");
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Shows a new tracker state, once the names of its system, station and ship are known.</summary>
    internal async void ShowTracking(TrackedCharacter? state)
    {
        // A slow lookup must not overwrite a newer state.
        var version = ++_trackingVersion;
        if (state is { Location: { } location })
        {
            // Player structures are not public; their names cannot be looked up.
            long?[] ids = [location.SolarSystemId, location.StationId, state.Ship?.TypeId];
            await _names.ResolveAsync(ids.OfType<long>());
        }

        if (version == _trackingVersion)
            Tracked = state;
    }

    partial void OnIsTrackedChanged(bool value) => _preferences.SetTracked(Id, value);

    /// <summary>How the character is marked on the map.</summary>
    [ObservableProperty]
    public partial CharacterMarker Marker { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkerGroups))]
    public partial bool IsPickingMarker { get; private set; }

    /// <summary>The markers to choose from, while the picker is open.</summary>
    public IReadOnlyList<MarkerGroupViewModel> MarkerGroups => IsPickingMarker
        ?
        [
            new("LETTERS", Choices(CharacterMarker.Letters)),
            new("NUMBERS", Choices(CharacterMarker.Numbers)),
            new("ICONS", Choices(CharacterMarker.Icons)),
        ]
        : [];

    [RelayCommand]
    private void ToggleMarkerPicker() => IsPickingMarker = !IsPickingMarker;

    private void SelectMarker(CharacterMarker marker)
    {
        _preferences.SetMarker(Id, marker);
        Marker = marker;
        IsPickingMarker = false;
    }

    private List<MarkerChoiceViewModel> Choices(IEnumerable<CharacterMarker> markers) =>
        [.. markers.Select(m => new MarkerChoiceViewModel(m, m == Marker, new RelayCommand(() => SelectMarker(m))))];

    // Created on the UI thread, so the continuation sets the property there too.
    private async Task LoadPortraitAsync(PortraitCache portraits) =>
        Portrait = await portraits.GetCharacterPortraitAsync(_character.Id);

    [RelayCommand]
    private Task ReauthAsync() => _page.ReauthAsync(this);

    [RelayCommand]
    private Task MakeMainAsync() => _page.MakeMainAsync(this);

    /// <summary>First click asks for confirmation, the second removes.</summary>
    [RelayCommand]
    private Task RemoveAsync()
    {
        if (IsConfirmingRemove)
            return _page.RemoveAsync(this);

        IsConfirmingRemove = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;
}

public sealed record MarkerGroupViewModel(string Label, IReadOnlyList<MarkerChoiceViewModel> Choices);

public sealed record MarkerChoiceViewModel(CharacterMarker Marker, bool IsSelected, IRelayCommand SelectCommand);

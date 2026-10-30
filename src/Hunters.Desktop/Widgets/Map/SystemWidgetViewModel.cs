using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// One system in detail, like the web's System panel (<c>SystemInfoPanel.tsx</c>): the selected system,
/// or the focused one while nothing is selected, tagged Selected and/or Focused. Its name, security and
/// region with links to zKillboard and Dotlan, Focus or Unfocus, and Destination: the main part for
/// every tracked character, ▾ for one usable character, the outcome under the buttons. Then the last
/// hour of NPC kills, ship kills, pod kills and jumps, once the server has activity, and every hunting
/// profile seen there (<c>/api/hunting/systems/{id}</c>, asked for while on screen), likely now first.
/// </summary>
public sealed partial class SystemWidgetViewModel : WidgetViewModelBase
{
    private readonly AccountPageViewModel _account;
    private readonly MapWidgetViewModel _map;
    private readonly Destinations _destinations;

    /// <summary>While on screen, the times ago ("12m") are written again every minute.</summary>
    private readonly DispatcherTimer _clock;

    /// <summary>The hunting answer for the shown system, and the session and system it is for; null without one.</summary>
    private (HuntingPlace Place, HuntersSession Session)? _hunting;

    /// <summary>Why the last hunting question failed, and for which system; null without a failure.</summary>
    private (int SystemId, string Message)? _huntingError;

    /// <summary>The hunting question on its way: the system and session it is for.</summary>
    private (int SystemId, HuntersSession Session, CancellationTokenSource Cancel)? _huntingLoading;

    public SystemWidgetViewModel(AccountPageViewModel account, MapWidgetViewModel map, Destinations destinations, CharacterTracking tracking)
    {
        _account = account;
        _map = map;
        _destinations = destinations;

        map.PropertyChanged += OnMapChanged;
        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(AccountPageViewModel.Session))
                return;
            SyncTracked();
            LoadHunting();
        };
        // Tracker events come from its polling threads.
        tracking.Changed += (_, _) => Dispatcher.UIThread.Post(SyncTracked);
        _clock = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _clock.Tick += (_, _) => ShowHunting();
        Sync();
        SyncTracked();
    }

    /// <summary>The system shown: the selected one, else the focused one; null with neither (or before the map loads).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSystem), nameof(IsSelected), nameof(IsFocused), nameof(FocusText), nameof(FocusTip), nameof(ZkillboardUrl), nameof(DotlanUrl))]
    public partial MapSystemInfo? System { get; private set; }

    public bool HasSystem => System is not null;

    /// <summary>The shown system's id; null with none.</summary>
    private int? SystemId => System is { } system && _map.Model is { } model ? model.Ids[system.Index] : null;

    public bool IsSelected => SystemId is { } id && id == _map.SelectedSystemId;

    public bool IsFocused => SystemId is { } id && id == _map.FocusedSystemId;

    public string FocusText => IsFocused ? "Unfocus" : "Focus";

    public string FocusTip => IsFocused
        ? "Clear the focus"
        : "Make this system the focus: the staging point for nearby intel and jump range";

    public Uri? ZkillboardUrl => SystemId is { } id ? SystemLinks.Zkillboard(id) : null;

    public Uri? DotlanUrl => System is { } system ? SystemLinks.Dotlan(system.Name) : null;

    /// <summary>The shown system's last hour (<see cref="SystemInfo.MetricRows"/>); empty without activity data.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLastHour))]
    public partial IReadOnlyList<MetricRow> LastHour { get; private set; } = [];

    public bool HasLastHour => LastHour.Count > 0;

    /// <summary>The server has hunting profiles: the section is shown for a system.</summary>
    public bool ShowsHunting => HasSystem && _map.HasHunting;

    /// <summary>The hunting profiles seen in the shown system, likely now first; empty while loading, on an error, or with none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProfiles))]
    public partial IReadOnlyList<SystemProfileRow> Profiles { get; private set; } = [];

    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>"Loading…", "None seen here.", or the error, in place of the profiles; null with profiles.</summary>
    [ObservableProperty]
    public partial string? ProfilesText { get; private set; }

    [ObservableProperty]
    public partial bool IsProfilesError { get; private set; }

    /// <summary>The shown system's destination and what it came to; kept while the same system is shown.</summary>
    [ObservableProperty]
    public partial DestinationViewModel? Destination { get; private set; }

    /// <summary>The tracked characters Destination sets it for.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTracked), nameof(TrackedCountText), nameof(DestinationTip))]
    public partial IReadOnlyList<DestinationCharacter> Tracked { get; private set; } = [];

    public bool HasTracked => Tracked.Count > 0;

    /// <summary>" 2" after DESTINATION: how many it is set for; empty without tracked characters.</summary>
    public string TrackedCountText => HasTracked ? $" {Tracked.Count}" : "";

    public string DestinationTip => HasTracked
        ? $"Set the autopilot destination here for every tracked character: {string.Join(", ", Tracked.Select(c => c.Name))}"
        : "No tracked characters: turn Track on for a character, or use ▾ for one";

    /// <summary>Who ▾ offers: the account's usable characters, online first, then the main. Empty while logged out.</summary>
    public IReadOnlyList<DestinationCharacter> UsableCharacters() =>
        _account.Session is { IsEnded: false } session ? _destinations.Usable(session.Account) : [];

    [RelayCommand]
    private void ToggleFocus()
    {
        if (IsFocused)
            _map.ClearSystem();
        else if (System is { } system)
            _map.FocusSystem(system.Index);
    }

    /// <summary>Destination's main part: every tracked character.</summary>
    [RelayCommand]
    private void SetDestination() => SetDestinationFor(Tracked);

    /// <summary>Sets the autopilot destination to the shown system for these characters.</summary>
    public void SetDestinationFor(IReadOnlyList<DestinationCharacter> characters)
    {
        if (Destination is { } destination)
            _ = destination.SetForAsync(characters);
    }

    private void OnMapChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapWidgetViewModel.SelectedSystemId) or nameof(MapWidgetViewModel.FocusedSystemId) or nameof(MapWidgetViewModel.Model))
            Sync();
        else if (e.PropertyName == nameof(MapWidgetViewModel.Activity))
            SyncLastHour();
        else if (e.PropertyName == nameof(MapWidgetViewModel.Hour))
            ShowHunting();
        else if (e.PropertyName == nameof(MapWidgetViewModel.HasHunting))
        {
            OnPropertyChanged(nameof(ShowsHunting));
            LoadHunting();
        }
    }

    private void Sync()
    {
        System = _map.SelectedSystem ?? _map.FocusedSystem;
        // Selected and focused can change without the shown system changing.
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(IsFocused));
        OnPropertyChanged(nameof(FocusText));
        OnPropertyChanged(nameof(FocusTip));

        if (SystemId is not { } id || System is not { } system)
            Destination = null;
        else if (Destination?.SystemId != id)
            Destination = new DestinationViewModel(id, system.Name, _destinations, () => _account.Session);
        SyncLastHour();
        OnPropertyChanged(nameof(ShowsHunting));
        LoadHunting();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName != nameof(IsActive))
            return;
        if (IsActive)
        {
            LoadHunting();
            ShowHunting();
            _clock.Start();
        }
        else
        {
            _clock.Stop();
        }
    }

    /// <summary>
    /// Asks for the shown system's hunting profiles while on screen, unless they are here or on their way
    /// for this system and session. A new system shows Loading until its answer comes.
    /// </summary>
    private void LoadHunting()
    {
        var session = _account.Session is { IsEnded: false } s ? s : null;
        if (SystemId is not { } id || session is null || !_map.HasHunting)
        {
            _huntingLoading?.Cancel.Cancel();
            _huntingLoading = null;
            _hunting = null;
            _huntingError = null;
            ShowHunting();
            return;
        }

        if (_hunting is { } answer && answer.Place.Id == id && answer.Session == session)
            return;
        if (_huntingLoading is { } loading && loading.SystemId == id && loading.Session == session)
            return;

        _hunting = null;
        _huntingError = null;
        ShowHunting();
        if (IsActive)
            _ = LoadHuntingAsync(session, id);
    }

    private async Task LoadHuntingAsync(HuntersSession session, int systemId)
    {
        _huntingLoading?.Cancel.Cancel();
        var cancel = new CancellationTokenSource();
        _huntingLoading = (systemId, session, cancel);
        try
        {
            var place = await session.GetSystemHuntingAsync(systemId, cancel.Token);
            if (cancel.IsCancellationRequested)
                return;
            _hunting = (place, session);
            ShowHunting();
        }
        catch (OperationCanceledException)
        {
            // Another system, or logged out.
        }
        catch (HuntersApiException e)
        {
            if (cancel.IsCancellationRequested)
                return;
            _huntingError = (systemId, e.Message);
            ShowHunting();
        }
        finally
        {
            if (_huntingLoading?.Cancel == cancel)
                _huntingLoading = null;
        }
    }

    /// <summary>The answer's profiles, likely now first, as of this hour and minute.</summary>
    private void ShowHunting()
    {
        IsProfilesError = _huntingError is { } error && error.SystemId == SystemId;
        if (IsProfilesError)
        {
            Profiles = [];
            ProfilesText = _huntingError!.Value.Message;
            return;
        }

        if (_hunting is not { } answer || answer.Place.Id != SystemId)
        {
            Profiles = [];
            ProfilesText = "Loading…";
            return;
        }

        var hour = _map.Hour;
        var now = DateTimeOffset.UtcNow;
        Profiles = [.. SystemInfo.SortProfiles(answer.Place.Profiles, hour).Select(p => SystemProfileRow.Create(p, _map.StyleOf(p.Profile), hour, now))];
        ProfilesText = Profiles.Count == 0 ? "None seen here." : null;
    }

    private void SyncLastHour() =>
        LastHour = System is { } system && _map.Model is { } model && MapFilters.ActivityFor(model, _map.Activity) is { } activity
            ? SystemInfo.MetricRows(activity, system.Index)
            : [];

    private void SyncTracked()
    {
        IReadOnlyList<DestinationCharacter> tracked = _account.Session is { IsEnded: false } session ? _destinations.Tracked(session.Account) : [];
        if (!tracked.SequenceEqual(Tracked))
            Tracked = tracked;
    }
}

/// <summary>A system's pages elsewhere, like the web's System panel links.</summary>
public static class SystemLinks
{
    public static Uri Zkillboard(int systemId) => new($"https://zkillboard.com/system/{systemId}/");

    /// <summary>Dotlan names systems with underscores for spaces.</summary>
    public static Uri Dotlan(string name) => new($"https://evemaps.dotlan.net/system/{Uri.EscapeDataString(name.Replace(' ', '_'))}");
}

/// <summary>A hunting profile seen in the shown system: its badge, name, status at this hour, and its pilots and last sighting.</summary>
/// <param name="Status">"peak now", "now" or "seen" (<see cref="SystemInfo.ProfileStatus"/>).</param>
/// <param name="Seen">"3 pilots · 2h": the pilots in 30 days and how long since it was last seen.</param>
public sealed record SystemProfileRow(ProfileStyle Style, string Name, string Status, string Seen, string SeenTip)
{
    public bool IsLikely => Status != SystemInfo.StatusSeen;

    public string StatusTip => IsLikely ? "Usually seen here at this hour (UTC)" : "Seen here, but not usually at this hour (UTC)";

    public static SystemProfileRow Create(HuntingPlaceProfile profile, ProfileStyle style, int hour, DateTimeOffset now)
    {
        var pilots = profile.Pilots switch
        {
            0 => "",
            1 => "1 pilot · ",
            var n => $"{n} pilots · ",
        };
        return new SystemProfileRow(
            style,
            profile.Name,
            SystemInfo.ProfileStatus(profile.Score, profile.Hours, hour),
            pilots + SystemInfo.TimeAgo(profile.LastSeen, now),
            $"Last seen {profile.LastSeen.UtcDateTime:yyyy-MM-dd HH:mm} UTC; {profile.Pilots} pilots in 30 days");
    }
}

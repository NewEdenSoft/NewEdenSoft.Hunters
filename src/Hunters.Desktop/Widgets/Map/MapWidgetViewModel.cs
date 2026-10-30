using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Contracts.Activity;
using NewEdenSoft.Hunters.Contracts.Discovery;
using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// The map of known space, as a widget: docked in the main window or floating as an overlay. The
/// map pack named by the server's discovery document is downloaded the first time the widget is on
/// screen in a session and kept in memory for the session. Tracked characters are marked on it at
/// their systems, with the markers chosen on the Account page. A click selects a system; the search
/// box selects one too and goes there. A system can be focused from its menu, the selected card or a double click,
/// or by itself as the followed character (or the main) moves far: the map goes there and rings it. The layout and colour are chosen
/// in the Map controls widget. Once the map is loaded, the server's activity is asked for every minute
/// while the widget is on screen (revalidated with its ETag, so mostly a 304), for the activity colours
/// and the hover panel; with hunting profiles shown, where they are seen too, for their glow and badges.
/// </summary>
public sealed partial class MapWidgetViewModel : WidgetViewModelBase
{
    /// <summary>How often to ask again while the server is still building the map pack.</summary>
    private static readonly TimeSpan PreparingRetry = TimeSpan.FromSeconds(10);

    /// <summary>How often to ask for new activity; the server answers 304 until there is a new hour.</summary>
    private static readonly TimeSpan IntelPoll = TimeSpan.FromMinutes(1);

    /// <summary>How often to ask where the shown hunting profiles are seen; the aggregates move every few minutes.</summary>
    private static readonly TimeSpan OverviewPoll = TimeSpan.FromMinutes(5);

    /// <summary>How often to ask the server again whether it has activity or hunting, while it has not.</summary>
    private static readonly TimeSpan FeaturesPoll = TimeSpan.FromMinutes(5);

    private readonly AccountPageViewModel _account;
    private readonly MapPreferences _preferences;
    private readonly ThemeCatalog _catalog;
    private readonly ThemeService _theme;
    private readonly CharacterTracking _tracking;
    private readonly CharacterPreferences _characters;
    private readonly MapFocus _focus;
    private readonly Destinations _destinations;

    /// <summary>A character focused before its location was known; the map moves there once it is.</summary>
    private long? _awaitingLocation;
    private CancellationTokenSource? _loading;
    private CancellationTokenSource? _polling;
    private Revalidated<ActivityCurrent>? _activity;
    private Revalidated<HuntingOverview>? _overview;
    private Dictionary<int, List<PlaceProfile>>? _profilesAt;

    /// <summary>Completed to start the next polling round now.</summary>
    private TaskCompletionSource _wake = new();
    private HuntersSession? _loadedFor;

    public MapWidgetViewModel(
        AccountPageViewModel account,
        MapPreferences preferences,
        ThemeCatalog catalog,
        ThemeService theme,
        CharacterTracking tracking,
        CharacterPreferences characters,
        MapFocus focus,
        Destinations destinations)
    {
        _account = account;
        _preferences = preferences;
        _catalog = catalog;
        _theme = theme;
        _tracking = tracking;
        _characters = characters;
        _focus = focus;
        _destinations = destinations;

        Search = new SystemSearchViewModel(Describe, FindSystem);

        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.Session))
            {
                EnsureLoaded();
                UpdateMarkers();
                focus.Clear();
                FocusedSystemId = null;
                SelectedSystemId = null;
            }
        };

        // Tracker events come from its polling threads.
        tracking.Changed += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            UpdateMarkers();
            OnCharacterMoved(e);
        });
        characters.MarkerChanged += (_, _) =>
        {
            UpdateMarkers();
            OnPropertyChanged(nameof(FocusedMarker));
        };
        focus.FocusRequested += (_, id) => FocusOn(id);
        // Quiet systems are drawn in the theme's line colour.
        theme.ThemeChanged += (_, _) => RebuildFilter();
        focus.PropertyChanged += (_, _) =>
        {
            if (focus.FocusedCharacterId is null)
            {
                FocusTarget = null;
                _awaitingLocation = null;
            }

            OnPropertyChanged(nameof(HasFocus));
            OnPropertyChanged(nameof(FocusedName));
            OnPropertyChanged(nameof(FocusedMarker));
            OnPropertyChanged(nameof(IsFollowing));
        };
        preferences.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MapPreferences.HuntingProfiles))
            {
                OnPropertyChanged(nameof(ShownProfiles));
                RebuildHunting();
                _wake.TrySetResult();
            }

            if (e.PropertyName == nameof(MapPreferences.Layout))
                OnPropertyChanged(nameof(Layout));

            if (e.PropertyName == nameof(MapPreferences.JumpRange))
                RebuildJumpRange();
        };
    }

    public MapLayout Layout => _preferences.Layout;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaded), nameof(IsNotLoaded))]
    public partial MapModel? Model { get; private set; }

    public SystemSearchViewModel Search { get; }

    /// <summary>The focused system: double-ringed on the map, and the origin for nearby intel. Kept for the session.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FocusedSystem), nameof(IsSelectionFocused))]
    public partial int? FocusedSystemId { get; private set; }

    /// <summary>
    /// Per system, whether a jump drive reaches it from the focused system (<see cref="JumpRanges.InRange"/>):
    /// drawn in blue. Null while the jump range is off or nothing is focused.
    /// </summary>
    [ObservableProperty]
    public partial bool[]? JumpRange { get; private set; }

    partial void OnFocusedSystemIdChanged(int? value) => RebuildJumpRange();

    private void RebuildJumpRange()
    {
        var setting = _preferences.JumpRange;
        JumpRange = setting.Enabled && Model is { } model && FocusedSystemId is { } id && model.IndexOf.TryGetValue(id, out var origin)
            ? JumpRanges.InRange(model, origin, setting.Ly)
            : null;
    }

    /// <summary>The focused system's name, security and region, shown in the search box; null when none is focused.</summary>
    public MapSystemInfo? FocusedSystem =>
        FocusedSystemId is { } id && Model is { } model && model.IndexOf.TryGetValue(id, out var index) ? Describe(index) : null;

    /// <summary>The selected system: the one last clicked or searched for, with a crosshair on the map and a card under the search. Null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedSystem), nameof(IsSelectionFocused), nameof(RouteToSystemId))]
    public partial int? SelectedSystemId { get; private set; }

    /// <summary>The system hovered in the Nearby widget (its card, by the pointer or the keyboard); null for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RouteToSystemId))]
    public partial int? NearbyHoveredSystemId { get; set; }

    /// <summary>Where the gate route from the focused system goes: the hovered Nearby card, else the selection. A system hovered on the map takes over.</summary>
    public int? RouteToSystemId => NearbyHoveredSystemId ?? SelectedSystemId;

    /// <summary>The selected system's name, security and region, for its card; null when none is selected.</summary>
    public MapSystemInfo? SelectedSystem =>
        SelectedSystemId is { } id && Model is { } model && model.IndexOf.TryGetValue(id, out var index) ? Describe(index) : null;

    /// <summary>The selected system is the focused one already: its card has no Focus button.</summary>
    public bool IsSelectionFocused => SelectedSystemId is not null && SelectedSystemId == FocusedSystemId;

    [RelayCommand]
    private void FocusSelection()
    {
        if (SelectedSystemId is { } id && Model is { } model && model.IndexOf.TryGetValue(id, out var index))
            FocusSystem(index);
    }

    /// <summary>A click on a system selects it; a click on empty space (-1) clears the selection.</summary>
    public void SelectSystem(int index) =>
        SelectedSystemId = Model is { } model && index >= 0 && index < model.Count ? model.Ids[index] : null;

    [RelayCommand]
    public void ClearSelection() => SelectedSystemId = null;

    /// <summary>A double click on a system focuses it, when the setting is on.</summary>
    public void DoubleClickSystem(int index)
    {
        if (_preferences.Behaviour.DoubleClickFocus)
            FocusSystem(index);
    }

    /// <summary>Selects a system from the search: the map goes there too, and the focus stays as it was.</summary>
    public void FindSystem(int index)
    {
        if (Model is not { } model || index < 0 || index >= model.Count)
            return;

        _focus.StopFollowing();
        _awaitingLocation = null;
        SelectedSystemId = model.Ids[index];
        FocusTarget = new MapFocusTarget(model.Ids[index], ZoomIn: true);
    }

    /// <summary>Focuses a system (its menu, the selected card, a double click): the map goes there, taking over from a followed character as dragging does.</summary>
    public void FocusSystem(int index)
    {
        if (Model is not { } model || index < 0 || index >= model.Count)
            return;

        _focus.StopFollowing();
        _awaitingLocation = null;
        FocusedSystemId = model.Ids[index];
        FocusTarget = new MapFocusTarget(model.Ids[index], ZoomIn: true);
    }

    [RelayCommand]
    public void ClearSystem() => FocusedSystemId = null;

    /// <summary>The account's tracked characters with a known location, main first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MapCharacterMarker> Markers { get; private set; } = [];

    /// <summary>Where the map should go next; a new instance for every request, so the view applies each one.</summary>
    [ObservableProperty]
    public partial MapFocusTarget? FocusTarget { get; private set; }

    /// <summary>A character is focused: the map shows its name with a follow button.</summary>
    public bool HasFocus => FocusedCharacter is not null;

    public string? FocusedName => FocusedCharacter?.Name;

    public CharacterMarker? FocusedMarker => FocusedCharacter is { } c ? _characters.GetMarker(c.Id, c.Name) : null;

    public bool IsFollowing => _focus.FollowedCharacterId is { } id && id == _focus.FocusedCharacterId;

    private Contracts.Account.CharacterInfo? FocusedCharacter =>
        _focus.FocusedCharacterId is { } id ? _account.Session?.Account.Characters.FirstOrDefault(c => c.Id == id) : null;

    /// <summary>A marker on the map was clicked.</summary>
    public void FocusCharacter(long characterId) => _focus.Focus(characterId);

    /// <summary>The user dragged the map, taking over from following.</summary>
    public void StopFollowing() => _focus.StopFollowing();

    [RelayCommand]
    private void ToggleFollow()
    {
        if (_focus.FocusedCharacterId is { } id)
            _focus.ToggleFollow(id);
    }

    [RelayCommand]
    private void DismissFocus() => _focus.Clear();

    private void FocusOn(long characterId)
    {
        if (_tracking.Get(characterId)?.Location is { } location)
        {
            _awaitingLocation = null;
            FocusTarget = new MapFocusTarget(location.SolarSystemId, ZoomIn: true);
        }
        else
        {
            _awaitingLocation = characterId;
        }
    }

    private void OnCharacterMoved(TrackedCharacterChangedEventArgs e)
    {
        if (e.Current?.Location is not { } location)
            return;

        if (AutoFocusSystem(e, location.SolarSystemId))
            return;

        if (e.CharacterId == _awaitingLocation)
        {
            _awaitingLocation = null;
            FocusTarget = new MapFocusTarget(location.SolarSystemId, ZoomIn: true);
        }
        else if (e.CharacterId == _focus.FollowedCharacterId && e.SystemChanged)
        {
            // Following keeps the user's zoom.
            FocusTarget = new MapFocusTarget(location.SolarSystemId, ZoomIn: false);
        }
    }

    /// <summary>
    /// The followed character, or the main while nobody is followed, came into known space from unknown
    /// space or moved far (Settings, "Map behaviour"): its system becomes the focus and the map goes
    /// there. Following carries on. True when it did.
    /// </summary>
    private bool AutoFocusSystem(TrackedCharacterChangedEventArgs e, int to)
    {
        var source = _focus.FollowedCharacterId ?? _account.Session?.Account.Characters.FirstOrDefault(c => c.IsMain)?.Id;
        if (Model is not { } model || e.CharacterId != source || !e.SystemChanged ||
            AutoFocus.Reason(model, e.Previous?.Location?.SolarSystemId, to, _preferences.Behaviour) is null)
        {
            return false;
        }

        FocusedSystemId = to;
        FocusTarget = new MapFocusTarget(to, ZoomIn: true);
        return true;
    }

    public bool IsLoaded => Model is not null;

    public bool IsNotLoaded => Model is null;

    [ObservableProperty]
    public partial MapFilter? Filter { get; private set; }

    /// <summary>The colour filter, chosen in the Map controls widget.</summary>
    [ObservableProperty]
    public partial MapFilterId FilterId { get; set; }

    /// <summary>Why the map is not shown: logged out, loading, being prepared, or an error.</summary>
    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool HasError { get; private set; }

    /// <summary>The system under the cursor, for the hover panel.</summary>
    [ObservableProperty]
    public partial MapSystemInfo? HoveredSystem { get; private set; }

    /// <summary>Called by the view as the cursor moves over systems.</summary>
    public void SetHover(int? index)
    {
        if (index is not { } i || Model is not { } model || i >= model.Count)
        {
            HoveredSystem = null;
            return;
        }

        var here = Markers.Where(m => m.SolarSystemId == model.Ids[i]).Select(m => m.IsOnline ? m.Name : $"{m.Name} (offline)").ToList();
        HoveredSystem = Describe(i) with
        {
            Characters = here.Count == 0 ? null : string.Join(", ", here),
            NpcKills = MapFilters.ActivityFor(model, Activity) is { } activity ? MapNpcKillsInfo.Create(activity.NpcKills, i) : null,
            Profiles = _profilesAt?.GetValueOrDefault(i),
            Distance = FocusedSystemId is { } focused && model.IndexOf.TryGetValue(focused, out var origin) && origin != i
                ? MapDistanceInfo.Create(model, origin, i, JumpRange)
                : null,
        };
    }

    /// <summary>The last "Navigate to" from a system's menu: its outcome is shown on the map. Null before the first.</summary>
    [ObservableProperty]
    public partial DestinationViewModel? Destination { get; private set; }

    /// <summary>
    /// A system's menu. Navigate to sets the destination through <paramref name="destination"/> (a Nearby
    /// card's, its outcome on the card), or else through <see cref="Destination"/>, the outcome on the map.
    /// Navigate to offers no one while logged out. Null for a system not on the map.
    /// </summary>
    public SystemMenu? MenuFor(int index, DestinationViewModel? destination = null)
    {
        if (Model is not { } model || index < 0 || index >= model.Count)
            return null;

        var (tracked, usable) = _account.Session is { IsEnded: false } session
            ? (_destinations.Tracked(session.Account), _destinations.Usable(session.Account))
            : ([], []);
        var systemId = model.Ids[index];
        return new SystemMenu(
            Describe(index),
            FocusedSystemId == systemId,
            tracked,
            usable,
            // The map shows one outcome at a time; a card shows its own.
            (destination ?? Destination)?.IsBusy == true,
            () => FocusSystem(index),
            ClearSystem,
            characters => NavigateTo(index, characters, destination));
    }

    /// <summary>Navigate to in a system's menu: sets the autopilot destination there for these characters.</summary>
    private void NavigateTo(int index, IReadOnlyList<DestinationCharacter> characters, DestinationViewModel? destination)
    {
        if (Model is not { } model || index < 0 || index >= model.Count || characters.Count == 0)
            return;

        if (destination is null)
        {
            if (Destination?.IsBusy == true)
                return;
            var systemId = model.Ids[index];
            if (Destination?.SystemId != systemId)
                Destination = new DestinationViewModel(systemId, model.Names[index], _destinations, () => _account.Session);
            destination = Destination;
        }
        _ = destination.SetForAsync(characters);
    }

    /// <summary>A system's name, security and region.</summary>
    private MapSystemInfo Describe(int index)
    {
        var model = Model!;
        var security = model.Security[index];
        return new MapSystemInfo(
            index,
            model.Names[index],
            SecurityStatus.Format(security),
            new SolidColorBrush(ThemeService.ToColor(_catalog.SecurityColor(security))),
            model.RegionName(index),
            null);
    }

    private void UpdateMarkers()
    {
        if (_account.Session is not { } session)
        {
            Markers = [];
            return;
        }

        var markers = new List<MapCharacterMarker>();
        foreach (var character in session.Account.Characters.OrderByDescending(c => c.IsMain).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            if (_tracking.Get(character.Id) is { Location: { } location } state)
            {
                markers.Add(new MapCharacterMarker(
                    character.Id, character.Name, location.SolarSystemId, _characters.GetMarker(character.Id, character.Name), state.IsOnline == true));
            }
        }

        // Unchanged positions (most tracker events) leave the map alone.
        if (!markers.SequenceEqual(Markers))
            Markers = markers;
    }

    [RelayCommand(CanExecute = nameof(HasError))]
    private void Retry()
    {
        _loadedFor = null;
        EnsureLoaded();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsActive) && IsActive)
        {
            EnsureLoaded();
            _wake.TrySetResult();
        }
    }

    /// <summary>The server has activity data: the activity colours are offered.</summary>
    [ObservableProperty]
    public partial bool HasActivity { get; private set; }

    /// <summary>The latest hour of activity; null until the server has sent some.</summary>
    [ObservableProperty]
    public partial ActivityCurrent? Activity { get; private set; }

    /// <summary>For NPC rising and falling: the minimum NPC kills in the last hour, chosen in Map controls.</summary>
    [ObservableProperty]
    public partial int MinNpc { get; set; }

    partial void OnFilterIdChanged(MapFilterId value) => RebuildFilter();

    partial void OnMinNpcChanged(int value) => RebuildFilter();

    partial void OnActivityChanged(ActivityCurrent? value) => RebuildFilter();

    /// <summary>The server has hunting profiles: they are offered in the Hunting widget.</summary>
    [ObservableProperty]
    public partial bool HasHunting { get; private set; }

    /// <summary>The server's hunting profiles; empty until it has sent them.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShownProfiles))]
    public partial IReadOnlyList<HuntingProfileInfo> HuntingProfiles { get; private set; } = [];

    /// <summary>The profiles shown on the map, in the order they were turned on: those chosen that the server has.</summary>
    public IReadOnlyList<string> ShownProfiles =>
        [.. _preferences.HuntingProfiles.Where(id => HuntingProfiles.Any(p => p.Id == id))];

    /// <summary>Where every profile is seen; null until fetched (only once one is shown).</summary>
    [ObservableProperty]
    public partial HuntingOverview? Overview { get; private set; }

    /// <summary>The hour of the day (UTC) the hunting profiles' "likely now" is for.</summary>
    [ObservableProperty]
    public partial int Hour { get; private set; } = DateTimeOffset.UtcNow.Hour;

    /// <summary>The glow and badges of the shown profiles; null when none are shown or there is no overview yet.</summary>
    [ObservableProperty]
    public partial HuntingLayer? Hunting { get; private set; }

    partial void OnHuntingProfilesChanged(IReadOnlyList<HuntingProfileInfo> value) => RebuildHunting();

    partial void OnOverviewChanged(HuntingOverview? value) => RebuildHunting();

    partial void OnHourChanged(int value) => RebuildHunting();

    /// <summary>Shows a hunting profile on the map, or hides it (the Hunting widget).</summary>
    public void ToggleHuntingProfile(string id) => _preferences.ToggleHuntingProfile(id);

    public ProfileStyle StyleOf(string id) => _catalog.ProfileStyle(id, NameOf(id));

    private string NameOf(string id) => HuntingProfiles.FirstOrDefault(p => p.Id == id)?.Name ?? id;

    private void RebuildHunting()
    {
        var shown = ShownProfiles;
        if (Model is not { } model || Overview is not { } overview || shown.Count == 0)
        {
            Hunting = null;
            _profilesAt = null;
            return;
        }

        Hunting = HuntingLayer.Build(model, overview, shown, Hour, StyleOf);
        _profilesAt = HuntingLayer.ProfilesBySystem(model, overview, shown, Hour, StyleOf, NameOf);
    }

    private void RebuildFilter()
    {
        var quiet = ThemeService.ToColor(ThemePalette.Resolve(_catalog, _theme.Current)["line"]);
        Filter = Model is { } model ? MapFilters.Create(FilterId, model, _catalog, new MapFilterContext(Activity, MinNpc, quiet)) : null;
    }

    partial void OnModelChanged(MapModel? value)
    {
        Search.SetModel(value);
        OnPropertyChanged(nameof(FocusedSystem));
        OnPropertyChanged(nameof(SelectedSystem));
        RebuildJumpRange();
        RebuildHunting();
    }

    /// <summary>Loads the map for the current session, the first time the widget is on screen in it.</summary>
    private void EnsureLoaded()
    {
        var session = _account.Session;
        if (session != _loadedFor)
        {
            // Another session, possibly on another server: drop the map of the last one.
            _loading?.Cancel();
            _polling?.Cancel();
            _polling = null;
            _loadedFor = null;
            _activity = null;
            Activity = null;
            HasActivity = false;
            _overview = null;
            Overview = null;
            HasHunting = false;
            HuntingProfiles = [];
            Model = null;
            Filter = null;
            HoveredSystem = null;
        }

        if (session is null)
        {
            HasError = false;
            StatusText = "Log in on the Account page to see the map.";
            return;
        }

        if (IsActive && _loadedFor is null)
            _ = LoadAsync(session);
    }

    /// <summary>
    /// Keeps the intel current while the map is loaded, each minute while on screen: the activity, the
    /// hunting profiles once, and where they are seen every five minutes while some are shown. While the
    /// server has no activity or hunting, it is asked every five minutes whether it has now. Turning a
    /// profile on or bringing the map on screen asks at once. Failures are left for the next round; the
    /// map stays as it is. The hour of the day (UTC) is checked each round too, for "likely now".
    /// </summary>
    private async Task PollIntelAsync(HuntersSession session, CancellationToken cancellationToken)
    {
        var featuresAskedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? overviewAskedAt = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Hour = DateTimeOffset.UtcNow.Hour;
                if (IsActive)
                {
                    try
                    {
                        if ((!HasActivity || !HasHunting) && DateTimeOffset.UtcNow - featuresAskedAt >= FeaturesPoll)
                        {
                            featuresAskedAt = DateTimeOffset.UtcNow;
                            var features = (await session.GetDiscoveryAsync(cancellationToken)).Features;
                            HasActivity = features.Contains(HuntersFeatures.Activity);
                            HasHunting = features.Contains(HuntersFeatures.Hunting);
                        }

                        if (HasActivity)
                        {
                            _activity = await session.GetActivityAsync(_activity, cancellationToken);
                            Activity = _activity.Value;
                        }

                        if (HasHunting && HuntingProfiles.Count == 0)
                            HuntingProfiles = await session.GetHuntingProfilesAsync(cancellationToken);

                        if (HasHunting && ShownProfiles.Count > 0 &&
                            (Overview is null || DateTimeOffset.UtcNow - overviewAskedAt >= OverviewPoll))
                        {
                            overviewAskedAt = DateTimeOffset.UtcNow;
                            _overview = await session.GetHuntingOverviewAsync(_overview, cancellationToken);
                            Overview = _overview.Value;
                        }
                    }
                    catch (HuntersApiException)
                    {
                        // Unreachable for a moment, or no data yet: try again next round.
                    }
                }

                await Task.WhenAny(Task.Delay(IntelPoll, cancellationToken), _wake.Task);
            }
        }
        catch (OperationCanceledException)
        {
            // Logged out or switched session.
        }
    }

    private async Task LoadAsync(HuntersSession session)
    {
        using var cancel = new CancellationTokenSource();
        _loading = cancel;
        _loadedFor = session;
        HasError = false;
        try
        {
            while (true)
            {
                StatusText = "Loading the map…";
                var discovery = await session.GetDiscoveryAsync(cancel.Token);
                if (discovery.MapPack is { } info)
                {
                    var pack = await session.GetMapPackAsync(info.SdeBuild, info.Format, cancel.Token);
                    var model = await Task.Run(() => MapModel.Build(pack), cancel.Token);
                    Model = model;
                    RebuildFilter();
                    StatusText = null;
                    HasActivity = discovery.Features.Contains(HuntersFeatures.Activity);
                    HasHunting = discovery.Features.Contains(HuntersFeatures.Hunting);
                    _polling = new CancellationTokenSource();
                    _ = PollIntelAsync(session, _polling.Token);
                    return;
                }

                // The server builds the map pack from the SDE after it starts.
                StatusText = "The server is preparing the map…";
                await Task.Delay(PreparingRetry, cancel.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Logged out or switched session.
        }
        catch (HuntersApiException e)
        {
            _loadedFor = null;
            StatusText = e.Message;
            HasError = true;
        }
        finally
        {
            if (_loading == cancel)
                _loading = null;
        }
    }
}

/// <param name="Index">The system's index on the map.</param>
/// <param name="Characters">The account's tracked characters in the system, or null.</param>
/// <param name="NpcKills">NPC kills in the last hour and how they compare; null without activity data.</param>
/// <param name="Profiles">The shown hunting profiles seen in the system, likely now first; null when none.</param>
/// <param name="Distance">How far the system is from the focused one; null without a focus, or for the focus itself.</param>
public sealed record MapSystemInfo(
    int Index,
    string Name,
    string Security,
    IBrush SecurityBrush,
    string Region,
    string? Characters,
    MapNpcKillsInfo? NpcKills = null,
    IReadOnlyList<PlaceProfile>? Profiles = null,
    MapDistanceInfo? Distance = null);

/// <summary>
/// The hover panel's distance line, with a focus: the gate jumps from the focused system (the shortest
/// way, as the route on the map) or "no gates", the light years ("5 jumps · 14.6 ly from Tama"), and
/// with the jump range on, whether a jump drive lands there: in range, out of range, or no cyno
/// (high-sec, Pochven, Zarzakh).
/// </summary>
/// <param name="Jumps">Gate jumps from the focused system; null where no gates lead.</param>
/// <param name="Status">With the jump range on: in range, out of range or no cyno; null otherwise.</param>
public sealed record MapDistanceInfo(int? Jumps, string Ly, string Origin, string? Status = null, bool IsInRange = false)
{
    public bool HasGates => Jumps is not null;

    public string JumpsText => Jumps switch
    {
        null => "no gates",
        1 => "1 jump",
        var n => $"{n} jumps",
    };

    public string FromText => $" from {Origin}";

    /// <summary>The status, set apart from the distance; empty without the jump range.</summary>
    public string StatusText => Status is null ? "" : $"  {Status}";

    /// <param name="inRange">With the jump range on, per system, whether it is in range of <paramref name="origin"/> (<see cref="JumpRanges.InRange"/>); null when off.</param>
    public static MapDistanceInfo Create(MapModel model, int origin, int index, bool[]? inRange)
    {
        var jumps = GateRoutes.Route(model, origin, index) is { } route ? route.Length - 1 : (int?)null;
        var ly = JumpRanges.FormatLy(Math.Round(model.LyBetween(origin, index), 2));
        if (inRange is null)
            return new MapDistanceInfo(jumps, ly, model.Names[origin]);

        var status = inRange[index] ? "in range" : !JumpRanges.IsCynoTarget(model, index) ? "no cyno" : "out of range";
        return new MapDistanceInfo(jumps, ly, model.Names[origin], status, inRange[index]);
    }
}

/// <summary>
/// The hover panel's NPC line: kills in the last hour, the change from the hour before, the usual for
/// this hour of the week, and whether the system is in its busy hours.
/// </summary>
public sealed record MapNpcKillsInfo(int LastHour, int? Change, string? Usual, string? Peak)
{
    public string ChangeText => Change switch
    {
        > 0 => $"▲ +{Change}",
        < 0 => $"▼ {Change}",
        0 => "±0",
        _ => "",
    };

    public bool IsRising => Change > 0;

    public bool IsFalling => Change < 0;

    public bool IsSame => Change == 0;

    public bool IsPeakNow => Peak == SystemInfo.PeakNow;

    public static MapNpcKillsInfo Create(ActivityColumns npcKills, int index)
    {
        var last = npcKills.LastHour[index];
        var peak = SystemInfo.PeakHint(npcKills.PeakRatio[index], npcKills.HoursToPeak[index]);
        var typical = npcKills.Typical?[index];
        return new MapNpcKillsInfo(
            last,
            npcKills.PreviousHour is { } previous ? last - previous[index] : null,
            typical is { } t ? $"usual {Math.Round(t):0}" : null,
            peak);
    }
}

/// <summary>A system to move the map to; <paramref name="ZoomIn"/> zooms in far enough to read system names.</summary>
public sealed record MapFocusTarget(int SolarSystemId, bool ZoomIn);

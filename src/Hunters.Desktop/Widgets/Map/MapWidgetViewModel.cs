using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// The map of known space, as a widget: docked in the main window or floating as an overlay. The
/// map pack named by the server's discovery document is downloaded the first time the widget is on
/// screen in a session and kept in memory for the session. Tracked characters are marked on it at
/// their systems, with the markers chosen on the Account page.
/// </summary>
public sealed partial class MapWidgetViewModel : WidgetViewModelBase
{
    /// <summary>How often to ask again while the server is still building the map pack.</summary>
    private static readonly TimeSpan PreparingRetry = TimeSpan.FromSeconds(10);

    private readonly AccountPageViewModel _account;
    private readonly MapPreferences _preferences;
    private readonly ThemeCatalog _catalog;
    private readonly CharacterTracking _tracking;
    private readonly CharacterPreferences _characters;
    private readonly MapFocus _focus;

    /// <summary>A character focused before its location was known; the map moves there once it is.</summary>
    private long? _awaitingLocation;
    private CancellationTokenSource? _loading;
    private HuntersSession? _loadedFor;

    public MapWidgetViewModel(
        AccountPageViewModel account,
        MapPreferences preferences,
        ThemeCatalog catalog,
        CharacterTracking tracking,
        CharacterPreferences characters,
        MapFocus focus)
    {
        _account = account;
        _preferences = preferences;
        _catalog = catalog;
        _tracking = tracking;
        _characters = characters;
        _focus = focus;

        LayoutChoices = preferences.CreateLayoutChoices();
        FilterChoices = MapFilters.All
            .Select(f => new ChoiceViewModel(f.Label, new RelayCommand(() => FilterId = f.Id)) { IsSelected = f.Id == FilterId })
            .ToList();

        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.Session))
            {
                EnsureLoaded();
                UpdateMarkers();
                focus.Clear();
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
            if (e.PropertyName == nameof(MapPreferences.Layout))
                OnPropertyChanged(nameof(Layout));
        };
    }

    public MapLayout Layout => _preferences.Layout;

    public IReadOnlyList<ChoiceViewModel> LayoutChoices { get; }

    public IReadOnlyList<ChoiceViewModel> FilterChoices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaded), nameof(IsNotLoaded))]
    public partial MapModel? Model { get; private set; }

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

    public bool IsLoaded => Model is not null;

    public bool IsNotLoaded => Model is null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Legend))]
    public partial MapFilter? Filter { get; private set; }

    [ObservableProperty]
    public partial MapFilterId FilterId { get; private set; }

    public IReadOnlyList<MapLegendItemViewModel> Legend =>
        Filter?.Legend.Select(item => new MapLegendItemViewModel(item.Label, new SolidColorBrush(item.Colour))).ToList() ?? [];

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

        var security = model.Security[i];
        var here = Markers.Where(m => m.SolarSystemId == model.Ids[i]).Select(m => m.IsOnline ? m.Name : $"{m.Name} (offline)").ToList();
        HoveredSystem = new MapSystemInfo(
            model.Names[i],
            SecurityStatus.Format(security),
            new SolidColorBrush(ThemeService.ToColor(_catalog.SecurityColor(security))),
            model.RegionName(i),
            here.Count == 0 ? null : string.Join(", ", here));
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
            EnsureLoaded();
    }

    partial void OnFilterIdChanged(MapFilterId value)
    {
        Filter = Model is { } model ? MapFilters.Create(value, model, _catalog) : null;
        for (var i = 0; i < MapFilters.All.Count; i++)
            FilterChoices[i].IsSelected = MapFilters.All[i].Id == value;
    }

    /// <summary>Loads the map for the current session, the first time the widget is on screen in it.</summary>
    private void EnsureLoaded()
    {
        var session = _account.Session;
        if (session != _loadedFor)
        {
            // Another session, possibly on another server: drop the map of the last one.
            _loading?.Cancel();
            _loadedFor = null;
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
                    var pack = await session.GetMapPackAsync(info.SdeBuild, cancel.Token);
                    var model = await Task.Run(() => MapModel.Build(pack), cancel.Token);
                    Model = model;
                    Filter = MapFilters.Create(FilterId, model, _catalog);
                    StatusText = null;
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

/// <param name="Characters">The account's tracked characters in the system, or null.</param>
public sealed record MapSystemInfo(string Name, string Security, IBrush SecurityBrush, string Region, string? Characters);

public sealed record MapLegendItemViewModel(string Label, IBrush Brush);

/// <summary>A system to move the map to; <paramref name="ZoomIn"/> zooms in far enough to read system names.</summary>
public sealed record MapFocusTarget(int SolarSystemId, bool ZoomIn);

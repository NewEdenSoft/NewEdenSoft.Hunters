using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Images;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Characters;

/// <summary>
/// The account's characters at a glance: marker, portrait, online, system and ship, live from the
/// tracker. Main first, then by name, so rows do not move as characters log in and out.
/// </summary>
public sealed partial class CharactersWidgetViewModel : WidgetViewModelBase
{
    private readonly AccountPageViewModel _account;
    private readonly CharacterTracking _tracking;
    private readonly CharacterPreferences _preferences;
    private readonly EveNames _names;
    private readonly PortraitCache _portraits;

    public CharactersWidgetViewModel(
        AccountPageViewModel account, CharacterTracking tracking, CharacterPreferences preferences, EveNames names, PortraitCache portraits)
    {
        _account = account;
        _tracking = tracking;
        _preferences = preferences;
        _names = names;
        _portraits = portraits;

        // The Account page rebuilds its list whenever the session or the account changes.
        account.Characters.CollectionChanged += OnAccountCharactersChanged;
        account.PropertyChanged += OnAccountChanged;

        // Tracker events come from its polling threads.
        tracking.Changed += (_, e) => Dispatcher.UIThread.Post(() => Find(e.CharacterId)?.ShowTracking(e.Current));
        preferences.MarkerChanged += (_, id) =>
        {
            if (Find(id) is { } row)
                row.Marker = preferences.GetMarker(row.Id, row.Name);
        };
        preferences.TrackingChanged += (_, id) =>
        {
            if (Find(id) is { } row)
                row.IsTracked = preferences.IsTracked(id);
        };

        Rebuild();
    }

    public ObservableCollection<CharacterRowViewModel> Characters { get; } = [];

    public bool IsLoggedIn => _account.IsLoggedIn;

    public bool IsLoggedOut => _account.IsLoggedOut;

    private CharacterRowViewModel? Find(long id) => Characters.FirstOrDefault(c => c.Id == id);

    private void OnAccountCharactersChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountPageViewModel.Session))
        {
            OnPropertyChanged(nameof(IsLoggedIn));
            OnPropertyChanged(nameof(IsLoggedOut));
        }
    }

    private void Rebuild()
    {
        var characters = _account.Session?.Account.Characters ?? [];
        var ordered = characters.OrderByDescending(c => c.IsMain).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (ordered.Select(c => (c.Id, c.Status)).SequenceEqual(Characters.Select(r => (r.Id, r.Status))))
            return;

        Characters.Clear();
        foreach (var character in ordered)
        {
            var row = new CharacterRowViewModel(character, _names)
            {
                Marker = _preferences.GetMarker(character.Id, character.Name),
                IsTracked = _preferences.IsTracked(character.Id),
            };
            row.LoadPortrait(_portraits);
            row.ShowTracking(_tracking.Get(character.Id));
            Characters.Add(row);
        }
    }
}

/// <summary>One character in the widget.</summary>
public sealed partial class CharacterRowViewModel(CharacterInfo character, EveNames names) : ObservableObject
{
    private int _trackingVersion;

    public long Id => character.Id;

    public string Name => character.Name;

    public CharacterStatus Status => character.Status;

    /// <summary>The server cannot get tokens for the character until the user logs in with it again.</summary>
    public bool NeedsLogin => character.Status != CharacterStatus.Active;

    [ObservableProperty]
    public partial CharacterMarker? Marker { get; set; }

    [ObservableProperty]
    public partial Bitmap? Portrait { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline), nameof(PlaceLine), nameof(ShipLine), nameof(HasProblem), nameof(Details))]
    public partial bool IsTracked { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnline), nameof(PlaceLine), nameof(ShipLine), nameof(HasProblem), nameof(Details))]
    public partial TrackedCharacter? Tracked { get; private set; }

    public bool IsOnline => !NeedsLogin && IsTracked && Tracked?.IsOnline == true;

    /// <summary>Needs a login, or polling fails.</summary>
    public bool HasProblem => NeedsLogin || (IsTracked && Tracked?.Status == TrackingStatus.Failing);

    /// <summary>"Jita", "Jita · Docked", or why there is no place to show.</summary>
    public string PlaceLine
    {
        get
        {
            if (NeedsLogin)
                return "Needs login";
            if (!IsTracked)
                return "Not tracked";
            if (Where is not { } where)
                return Tracked?.Status == TrackingStatus.Failing ? "Location unavailable" : "Locating…";
            return where.Docked is null ? where.System : $"{where.System} · Docked";
        }
    }

    /// <summary>The ship, or null when unknown or not shown.</summary>
    public string? ShipLine => NeedsLogin || !IsTracked ? null : Where?.Ship;

    /// <summary>The tooltip: the full station name and the last error.</summary>
    public string Details
    {
        get
        {
            string?[] parts = [Name, IsOnline ? "Online" : null, PlaceLine, Where?.Docked, ShipLine, IsTracked ? Tracked?.Error : null];
            return string.Join("\n", parts.OfType<string>().Distinct());
        }
    }

    private CharacterWhereabouts? Where => CharacterWhereabouts.From(Tracked, names);

    /// <summary>Shows a new tracker state once its names are known; a slow lookup never overwrites a newer state.</summary>
    internal async void ShowTracking(TrackedCharacter? state)
    {
        var version = ++_trackingVersion;
        await CharacterWhereabouts.ResolveNamesAsync(state, names);
        if (version == _trackingVersion)
            Tracked = state;
    }

    // Created on the UI thread, so the continuation sets the property there too.
    internal async void LoadPortrait(PortraitCache portraits) =>
        Portrait = await portraits.GetCharacterPortraitAsync(character.Id);
}

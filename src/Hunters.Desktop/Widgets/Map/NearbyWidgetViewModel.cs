using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Contracts.Intel;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// What is in reach of the focused system, from the server's <c>/api/intel/reach</c>, like the web's
/// Nearby panel (<c>NearbyPanel.tsx</c>): the cyno targets within the Jump range widget's light years,
/// cut here to the gate jumps chosen, sorted by the chance of a target (here), the rise in NPC kills,
/// the most NPC kills, or a hunting profile's "likely now" (<see cref="MapPreferences.Nearby"/>). Asked
/// for while the widget is on screen, each time the focus, the light years or the sort changes; the gate
/// jumps and the jump range toggle only change what is shown. Systems where nothing happens
/// (<see cref="Nearby.IsActive"/>) are hidden unless asked for. Up to <see cref="Nearby.Limit"/> cards,
/// each with its kills, the profiles seen, and quick controls: the destination for every tracked
/// character, and focus.
/// </summary>
public sealed partial class NearbyWidgetViewModel : WidgetViewModelBase
{
    private readonly AccountPageViewModel _account;
    private readonly MapWidgetViewModel _map;
    private readonly MapPreferences _preferences;
    private readonly ThemeCatalog _catalog;
    private readonly Destinations _destinations;
    private CancellationTokenSource? _loading;

    /// <summary>The question the rows answer, so the same one isn't asked twice.</summary>
    private (NearbyRequest Request, HuntersSession Session)? _loadedFor;

    /// <summary>Set while the sort list is rebuilt, so the list's own selection changes aren't taken as the user's.</summary>
    private bool _syncingSort;

    /// <summary>The last answer's cards, quiet ones too, in the server's order, and the origin's name; null without one.</summary>
    private (IReadOnlyList<NearbyCardViewModel> Cards, string Origin)? _answer;

    /// <summary>Each listed system's destination, kept while it stays in the answers.</summary>
    private Dictionary<int, DestinationViewModel> _destinationOf = [];

    /// <summary>The tracked characters the Dest button sets the destination for.</summary>
    private IReadOnlyList<DestinationCharacter> _tracked = [];

    public NearbyWidgetViewModel(
        AccountPageViewModel account,
        MapWidgetViewModel map,
        MapPreferences preferences,
        ThemeCatalog catalog,
        CharacterTracking tracking,
        Destinations destinations)
    {
        _account = account;
        _map = map;
        _preferences = preferences;
        _catalog = catalog;
        _destinations = destinations;

        int?[] jumps = [null, .. Nearby.JumpChoices.Select(n => (int?)n)];
        JumpChoices = jumps
            .Select(n => new ChoiceViewModel(n is { } j ? $"{j}" : "Off", new RelayCommand(() => preferences.Nearby = preferences.Nearby with { Jumps = n })))
            .ToList();
        SyncControls();
        SyncSortOptions();

        map.PropertyChanged += OnMapChanged;
        preferences.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(MapPreferences.Nearby) or nameof(MapPreferences.JumpRange)))
                return;
            SyncControls();
            SyncSortOptions();
            Refresh();
            ShowAnswer();
        };
        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.Session))
                SyncTracked();
        };
        // Tracker events come from its polling threads.
        tracking.Changed += (_, _) => Dispatcher.UIThread.Post(SyncTracked);
    }

    /// <summary>Off, or the gate jumps offered.</summary>
    public IReadOnlyList<ChoiceViewModel> JumpChoices { get; }

    public bool ByJumpRange => _preferences.Nearby.ByJumpRange;

    /// <summary>"On · 8 ly" or "Off · 8 ly": the Jump range widget's light years.</summary>
    public string JumpRangeText => $"{(ByJumpRange ? "On" : "Off")} · {JumpRanges.FormatLy(_preferences.JumpRange.Ly)}";

    /// <summary>Chance of a target, NPC kills rising, NPC kills, and each hunting profile's "likely now".</summary>
    [ObservableProperty]
    public partial IReadOnlyList<NearbySortOption> SortOptions { get; private set; } = [];

    [ObservableProperty]
    public partial NearbySortOption? SelectedSort { get; set; }

    partial void OnSelectedSortChanged(NearbySortOption? value)
    {
        if (!_syncingSort && value is not null && value.Id != EffectiveSort)
            _preferences.Nearby = _preferences.Nearby with { Sort = value.Id };
    }

    /// <summary>The cards shown, in the server's order or by the chance of a target; empty until loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial IReadOnlyList<NearbyCardViewModel> Rows { get; private set; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Why there are no rows: no focus, nothing to search by, loading, or an error; null with rows.</summary>
    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    [ObservableProperty]
    public partial bool HasError { get; private set; }

    /// <summary>"12 active near Tama, NPC kills rising first." ("(first 50)" when there are more).</summary>
    [ObservableProperty]
    public partial string? Summary { get; private set; }

    /// <summary>"8 quiet hidden", or "hide 8 quiet" while they are shown; null when none are quiet.</summary>
    [ObservableProperty]
    public partial string? QuietText { get; private set; }

    [RelayCommand]
    private void ToggleQuiet() => _preferences.Nearby = _preferences.Nearby with { ShowQuiet = !_preferences.Nearby.ShowQuiet };

    [RelayCommand]
    private void ToggleJumpRange() => _preferences.Nearby = _preferences.Nearby with { ByJumpRange = !ByJumpRange };

    /// <summary>A card's Dest: the autopilot destination for every tracked character, the outcome on the card.</summary>
    [RelayCommand]
    private void SetDestination(NearbyCardViewModel card) => _ = card.Destination.SetForAsync(_tracked);

    /// <summary>A card's Focus: look around that system instead.</summary>
    [RelayCommand]
    private void FocusCard(NearbyCardViewModel card)
    {
        if (_map.Model is { } model && model.IndexOf.TryGetValue(card.SystemId, out var index))
            _map.FocusSystem(index);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsActive) && IsActive)
            Refresh();
    }

    private IReadOnlyList<string> ProfileIds => [.. _map.HuntingProfiles.Select(p => p.Id)];

    /// <summary>The sort in use: the saved one, or activity for a profile the server doesn't list.</summary>
    private string EffectiveSort
    {
        get
        {
            var sort = _preferences.Nearby.Sort;
            return sort is Nearby.SortActivity or Nearby.SortNpcKills or Nearby.SortTarget || ProfileIds.Contains(sort) ? sort : Nearby.SortActivity;
        }
    }

    private void OnMapChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapWidgetViewModel.HuntingProfiles))
            SyncSortOptions();
        if (e.PropertyName is nameof(MapWidgetViewModel.FocusedSystemId) or nameof(MapWidgetViewModel.Model) or nameof(MapWidgetViewModel.HuntingProfiles))
            Refresh();
    }

    private void SyncControls()
    {
        var jumps = _preferences.Nearby.Jumps;
        for (var i = 0; i < JumpChoices.Count; i++)
            JumpChoices[i].IsSelected = i == 0 ? jumps is null : Nearby.JumpChoices[i - 1] == jumps;
        OnPropertyChanged(nameof(ByJumpRange));
        OnPropertyChanged(nameof(JumpRangeText));
    }

    private void SyncSortOptions()
    {
        _syncingSort = true;
        try
        {
            SortOptions =
            [
                new NearbySortOption(Nearby.SortTarget, "Chance of a target"),
                new NearbySortOption(Nearby.SortActivity, "NPC kills rising"),
                new NearbySortOption(Nearby.SortNpcKills, "NPC kills"),
                .. _map.HuntingProfiles.Select(p => new NearbySortOption(p.Id, $"{p.Name}: likely now")),
            ];
            SelectedSort = SortOptions.FirstOrDefault(o => o.Id == EffectiveSort);
        }
        finally
        {
            _syncingSort = false;
        }
    }

    /// <summary>The cards' Dest buttons follow the tracked characters; most tracker changes (a move, a poll) leave them as they were.</summary>
    private void SyncTracked()
    {
        IReadOnlyList<DestinationCharacter> tracked = _account.Session is { IsEnded: false } session ? _destinations.Tracked(session.Account) : [];
        if (tracked.SequenceEqual(_tracked))
            return;
        _tracked = tracked;
        ShowAnswer();
    }

    /// <summary>Asks again when the question or the session changed, while on screen.</summary>
    private void Refresh()
    {
        var session = _account.Session;
        if (_map.Model is not { } model || session is null || _map.FocusedSystemId is not { } from || !model.IndexOf.ContainsKey(from))
        {
            Clear("Focus a system (double click it) to see what is near it.");
            return;
        }

        if (Nearby.Request(_preferences.Nearby, from, _preferences.JumpRange.Ly, ProfileIds) is not { } request)
        {
            Clear("Choose gate jumps, the jump range, or both.");
            return;
        }

        if (!IsActive || _loadedFor == (request, session))
            return;

        _ = LoadAsync(session, model, request);
    }

    private void Clear(string status)
    {
        _loading?.Cancel();
        _loadedFor = null;
        _answer = null;
        Rows = [];
        Summary = null;
        QuietText = null;
        HasError = false;
        StatusText = status;
    }

    /// <summary>
    /// The last answer's cards within the gate jumps chosen, by the chance of a target when sorting by it,
    /// without the quiet ones unless they are asked for, up to <see cref="Nearby.Limit"/>, each with its
    /// distance as the setting shows it and its Dest button for the tracked characters.
    /// </summary>
    private void ShowAnswer()
    {
        if (_answer is not { } answer)
            return;

        var setting = _preferences.Nearby;
        var sort = EffectiveSort;
        var byTarget = sort == Nearby.SortTarget;
        var byJumps = setting.Jumps is not null;
        double? ly = setting.ByJumpRange ? _preferences.JumpRange.Ly : null;
        var trackedNames = string.Join(", ", _tracked.Select(c => c.Name));
        var within = answer.Cards.Where(c => Nearby.WithinJumps(c.Jumps, setting.Jumps));
        IReadOnlyList<NearbyCardViewModel> around = [.. (byTarget ? Nearby.ByTargetChance(within, c => (c.Chance, c.Jumps, c.Ly)) : within)
            .Select(c => c with
            {
                Distance = Nearby.DistanceText(c.Jumps, c.Ly, byJumps, ly is not null),
                InJumpRange = Nearby.InJumpRange(c.CynoTarget, c.Ly, ly),
                DistanceTip = Nearby.DistanceTip(c.CynoTarget, c.Ly, ly),
                ShowChance = byTarget,
                TrackedCount = _tracked.Count,
                DestinationTip = _tracked.Count == 0
                    ? "No tracked characters"
                    : $"Set the autopilot destination to {c.Name} for every tracked character: {trackedNames}",
            })];
        var active = around.Count(c => c.IsActive);
        var quiet = around.Count - active;
        var showQuiet = setting.ShowQuiet;
        var listed = showQuiet ? around : [.. around.Where(c => c.IsActive)];
        Rows = [.. listed.Take(Nearby.Limit)];
        var order = sort switch
        {
            Nearby.SortTarget => "best chance of a target first",
            Nearby.SortNpcKills => "most NPC kills first",
            Nearby.SortActivity => "NPC kills rising first",
            _ => $"{_map.HuntingProfiles.FirstOrDefault(p => p.Id == sort)?.Name ?? sort} likely now first",
        };
        var more = Rows.Count < listed.Count ? $" (first {Nearby.Limit})" : "";
        Summary = $"{active} active near {answer.Origin}, {order}{more}.";
        QuietText = quiet == 0 ? null : showQuiet ? $"hide {quiet} quiet" : $"{quiet} quiet hidden";
        StatusText = Rows.Count == 0 ? "Nothing happening in reach." : null;
    }

    private async Task LoadAsync(HuntersSession session, MapModel model, NearbyRequest request)
    {
        _loading?.Cancel();
        using var cancel = new CancellationTokenSource();
        _loading = cancel;
        _loadedFor = (request, session);
        var name = model.Names[model.IndexOf[request.From]];
        if (Rows.Count == 0)
            StatusText = $"Looking around {name}…";
        HasError = false;
        try
        {
            var reach = await session.GetReachAsync(request.From, null, request.Ly, request.Profile, request.Limit, request.Sort, cancel.Token);
            var destinations = new Dictionary<int, DestinationViewModel>();
            IReadOnlyList<NearbyCardViewModel> cards = [.. reach.Systems
                .Where(s => s.SystemId != reach.From && model.IndexOf.ContainsKey(s.SystemId))
                .Select(s => Card(s, model.Names[model.IndexOf[s.SystemId]], request.Profile, destinations))];
            _destinationOf = destinations;
            _tracked = _destinations.Tracked(session.Account);
            _answer = (cards, name);
            ShowAnswer();
        }
        catch (OperationCanceledException)
        {
            // Another question, or logged out.
        }
        catch (HuntersApiException e)
        {
            _loadedFor = null;
            _answer = null;
            Rows = [];
            Summary = null;
            QuietText = null;
            StatusText = e.Message;
            HasError = true;
        }
        finally
        {
            if (_loading == cancel)
                _loading = null;
        }
    }

    /// <summary>One system's card, keeping its destination from the answer before.</summary>
    private NearbyCardViewModel Card(ReachSystem s, string name, string? sortProfile, Dictionary<int, DestinationViewModel> destinations)
    {
        var destination = _destinationOf.GetValueOrDefault(s.SystemId) ?? new DestinationViewModel(s.SystemId, name, _destinations, () => _account.Session);
        destinations[s.SystemId] = destination;

        NearbyNpc? npc = null;
        NearbyPvp? pvp = null;
        if (s.Activity is { } activity)
        {
            var change = Nearby.NpcChange(activity.NpcKills);
            npc = new NearbyNpc(
                activity.NpcKills.LastHour,
                change is > 0 ? $"▲{change}" : change is < 0 ? $"▼{-change}" : null,
                change > 0,
                $"{SystemInfo.FormatIsk(activity.NpcKills.Day)}/24h",
                $"NPC kills: last hour{(change is null ? "" : ", change since the hour before")}, last 24 hours");
            var day = activity.ShipKills.Day + activity.PodKills.Day;
            if (day > 0)
                pvp = new NearbyPvp(activity.ShipKills.LastHour + activity.PodKills.LastHour, $"{day}/24h");
        }

        IReadOnlyList<NearbyBadge> badges = [.. Nearby.SeenFirst(s.Profiles).Take(6).Select(p =>
        {
            var profile = _map.HuntingProfiles.FirstOrDefault(i => i.Id == p.Profile)?.Name ?? p.Profile;
            return new NearbyBadge(_map.StyleOf(p.Profile), $"{profile}: {Nearby.ProfileWhen(p)}", p.LikelyNow > 0, p.Profile == sortProfile);
        })];

        return new NearbyCardViewModel(
            s.SystemId,
            name,
            SecurityStatus.Format(s.Security),
            new SolidColorBrush(ThemeService.ToColor(_catalog.SecurityColor(s.Security))),
            s.Jumps,
            s.Ly,
            s.CynoTarget,
            Nearby.IsActive(s.Activity, s.Profiles.Count),
            Nearby.TargetChance(s.Activity, s.Profiles),
            npc,
            pvp,
            badges,
            destination);
    }
}

/// <summary>One system in reach: where it is, what happens there, the profiles seen, and its quick controls.</summary>
/// <param name="Jumps">Gate jumps from the focus; null with no gate route.</param>
/// <param name="IsActive">Something happens there (<see cref="Nearby.IsActive"/>); quiet systems are hidden unless asked for.</param>
/// <param name="Chance">The chance of a target there now, 0–1 (<see cref="Nearby.TargetChance"/>).</param>
/// <param name="Npc">NPC kills; null without activity data.</param>
/// <param name="Pvp">Ship and pod kills; null with none in the last 24 hours.</param>
/// <param name="Badges">The profiles seen there, likely now first, up to six.</param>
public sealed record NearbyCardViewModel(
    int SystemId,
    string Name,
    string Security,
    IBrush SecurityBrush,
    int? Jumps,
    double Ly,
    bool CynoTarget,
    bool IsActive,
    double Chance,
    NearbyNpc? Npc,
    NearbyPvp? Pvp,
    IReadOnlyList<NearbyBadge> Badges,
    DestinationViewModel Destination)
{
    /// <summary>"3 j", "4.2 ly" or both, as the setting shows it (<see cref="Nearby.DistanceText"/>).</summary>
    public string Distance { get; init; } = "";

    /// <summary>A cyno target within the light years shown: the distance is in the jump range blue.</summary>
    public bool InJumpRange { get; init; }

    /// <summary>In or out of jump range, or no cyno; null while the light years aren't shown.</summary>
    public string? DistanceTip { get; init; }

    /// <summary>The chance is shown while sorting by it.</summary>
    public bool ShowChance { get; init; }

    /// <summary>"63%".</summary>
    public string ChanceText => $"{Math.Round(Chance * 100, MidpointRounding.AwayFromZero)}%";

    /// <summary>At least even: shown in the ok colour.</summary>
    public bool IsLikely => Chance >= 0.5;

    public bool HasActivity => Npc is not null;

    public bool HasPvp => Pvp is not null;

    public bool HasBadges => Badges.Count > 0;

    /// <summary>How many tracked characters Dest sets the destination for.</summary>
    public int TrackedCount { get; init; }

    public bool HasTracked => TrackedCount > 0;

    public string DestinationTip { get; init; } = "";

    public string FocusTip => $"Focus {Name}: look around it instead";
}

/// <summary>NPC kills on a card: the last hour, its change from the hour before ("▲3", "▼2"; null for none or unknown), and the last 24 hours ("1.2k/24h").</summary>
public sealed record NearbyNpc(int LastHour, string? Change, bool IsRising, string Day, string Tip)
{
    public bool HasChange => Change is not null;
}

/// <summary>Ship and pod kills on a card: the last hour and the last 24 hours ("4/24h").</summary>
public sealed record NearbyPvp(int LastHour, string Day)
{
    /// <summary>Kills in the last hour: shown in the warning colour.</summary>
    public bool IsHot => LastHour > 0;
}

/// <summary>A hunting profile's badge on a card: bright when likely now, outlined when the list is sorted by it.</summary>
public sealed record NearbyBadge(ProfileStyle Style, string Tip, bool IsNow, bool IsSorted)
{
    public double Opacity => IsNow ? 1 : 0.4;
}

/// <summary>One way to sort the Nearby rows: <see cref="Nearby.SortActivity"/>, <see cref="Nearby.SortNpcKills"/> or a profile's id.</summary>
public sealed record NearbySortOption(string Id, string Label)
{
    public override string ToString() => Label;
}

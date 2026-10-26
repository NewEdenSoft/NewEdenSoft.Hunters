using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Contracts.Intel;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// What is in reach of the focused system, from the server's <c>/api/intel/reach</c>, like the web's
/// Nearby panel (<c>NearbyPanel.tsx</c>): the cyno targets within the Jump range widget's light years,
/// cut here to the gate jumps chosen, sorted by the chance of a target (here), the rise in NPC kills, the
/// most NPC kills, or a hunting profile's "likely now" (<see cref="MapPreferences.Nearby"/>). Asked for while the widget is on screen,
/// each time the focus, the light years or the sort changes; the gate jumps and the jump range toggle
/// only change what is shown. Systems where nothing happens (<see cref="Nearby.IsActive"/>) are hidden
/// unless asked for.
/// </summary>
public sealed partial class NearbyWidgetViewModel : WidgetViewModelBase
{
    private readonly AccountPageViewModel _account;
    private readonly MapWidgetViewModel _map;
    private readonly MapPreferences _preferences;
    private readonly ThemeCatalog _catalog;
    private CancellationTokenSource? _loading;

    /// <summary>The question the rows answer, so the same one isn't asked twice.</summary>
    private (NearbyRequest Request, HuntersSession Session)? _loadedFor;

    /// <summary>Set while the sort list is rebuilt, so the list's own selection changes aren't taken as the user's.</summary>
    private bool _syncingSort;

    /// <summary>The last answer's rows, quiet ones too, in the server's order, and the origin's name; null without one.</summary>
    private (IReadOnlyList<NearbyRowViewModel> Rows, string Origin)? _answer;

    public NearbyWidgetViewModel(AccountPageViewModel account, MapWidgetViewModel map, MapPreferences preferences, ThemeCatalog catalog)
    {
        _account = account;
        _map = map;
        _preferences = preferences;
        _catalog = catalog;

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

    /// <summary>The systems in reach, in the server's order or by the chance of a target; empty until loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial IReadOnlyList<NearbyRowViewModel> Rows { get; private set; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Why there are no rows: no focus, nothing to search by, loading, or an error; null with rows.</summary>
    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    [ObservableProperty]
    public partial bool HasError { get; private set; }

    /// <summary>"12 active near Tama, NPC kills rising first."</summary>
    [ObservableProperty]
    public partial string? Summary { get; private set; }

    /// <summary>"8 quiet hidden", or "hide 8 quiet" while they are shown; null when none are quiet.</summary>
    [ObservableProperty]
    public partial string? QuietText { get; private set; }

    [RelayCommand]
    private void ToggleQuiet() => _preferences.Nearby = _preferences.Nearby with { ShowQuiet = !_preferences.Nearby.ShowQuiet };

    [RelayCommand]
    private void ToggleJumpRange() => _preferences.Nearby = _preferences.Nearby with { ByJumpRange = !ByJumpRange };

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
    /// The last answer's rows within the gate jumps chosen, by the chance of a target when sorting by it,
    /// without the quiet ones unless they are asked for, each with its distance as the setting shows it.
    /// </summary>
    private void ShowAnswer()
    {
        if (_answer is not { } answer)
            return;

        var setting = _preferences.Nearby;
        var sort = EffectiveSort;
        var byTarget = sort == Nearby.SortTarget;
        var (byJumps, byLy) = (setting.Jumps is not null, setting.ByJumpRange);
        var within = answer.Rows.Where(r => Nearby.WithinJumps(r.Jumps, setting.Jumps));
        IReadOnlyList<NearbyRowViewModel> around = [.. (byTarget ? Nearby.ByTargetChance(within, r => (r.Chance, r.Jumps, r.Ly)) : within)
            .Select(r => r with { Distance = Nearby.DistanceText(r.Jumps, r.Ly, byJumps, byLy), ShowChance = byTarget })];
        var active = around.Count(r => r.IsActive);
        var quiet = around.Count - active;
        var showQuiet = setting.ShowQuiet;
        Rows = showQuiet ? around : [.. around.Where(r => r.IsActive)];
        var order = sort switch
        {
            Nearby.SortTarget => "best chance of a target first",
            Nearby.SortNpcKills => "most NPC kills first",
            Nearby.SortActivity => "NPC kills rising first",
            _ => $"{_map.HuntingProfiles.FirstOrDefault(p => p.Id == sort)?.Name ?? sort} likely now first",
        };
        Summary = $"{active} active near {answer.Origin}, {order}.";
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
            IReadOnlyList<NearbyRowViewModel> rows = [.. reach.Systems
                .Where(s => s.SystemId != reach.From && model.IndexOf.ContainsKey(s.SystemId))
                .Select(s => new NearbyRowViewModel(
                    s.SystemId,
                    model.Names[model.IndexOf[s.SystemId]],
                    SecurityStatus.Format(s.Security),
                    new SolidColorBrush(ThemeService.ToColor(_catalog.SecurityColor(s.Security))),
                    s.Jumps,
                    s.Ly,
                    Nearby.IsActive(s.Activity, s.Profiles.Count),
                    Nearby.TargetChance(s.Activity, s.Profiles)))];
            _answer = (rows, name);
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
}

/// <summary>One system in reach: its name, security and how far it is.</summary>
/// <param name="Jumps">Gate jumps from the focus; null with no gate route.</param>
/// <param name="IsActive">Something happens there (<see cref="Nearby.IsActive"/>); quiet systems are hidden unless asked for.</param>
/// <param name="Chance">The chance of a target there now, 0–1 (<see cref="Nearby.TargetChance"/>).</param>
public sealed record NearbyRowViewModel(int SystemId, string Name, string Security, IBrush SecurityBrush, int? Jumps, double Ly, bool IsActive, double Chance)
{
    /// <summary>"3 j", "4.2 ly" or both, as the setting shows it (<see cref="Nearby.DistanceText"/>).</summary>
    public string Distance { get; init; } = "";

    /// <summary>The chance is shown while sorting by it.</summary>
    public bool ShowChance { get; init; }

    /// <summary>"63%".</summary>
    public string ChanceText => $"{Math.Round(Chance * 100, MidpointRounding.AwayFromZero)}%";

    /// <summary>At least even: shown in the ok colour.</summary>
    public bool IsLikely => Chance >= 0.5;
}

/// <summary>One way to sort the Nearby rows: <see cref="Nearby.SortActivity"/>, <see cref="Nearby.SortNpcKills"/> or a profile's id.</summary>
public sealed record NearbySortOption(string Id, string Label)
{
    public override string ToString() => Label;
}

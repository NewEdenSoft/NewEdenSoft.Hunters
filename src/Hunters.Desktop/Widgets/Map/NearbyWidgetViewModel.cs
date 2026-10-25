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
/// Nearby panel (<c>NearbyPanel.tsx</c>): within gate jumps and/or the Jump range widget's light years,
/// sorted by the rise in NPC kills, the most NPC kills, or a hunting profile's "likely now"
/// (<see cref="MapPreferences.Nearby"/>). Asked for while the widget is on screen, each time the focus
/// or the setting changes.
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
        };
    }

    /// <summary>Off, or the gate jumps offered.</summary>
    public IReadOnlyList<ChoiceViewModel> JumpChoices { get; }

    public bool ByJumpRange => _preferences.Nearby.ByJumpRange;

    /// <summary>"On · 8 ly" or "Off · 8 ly": the Jump range widget's light years.</summary>
    public string JumpRangeText => $"{(ByJumpRange ? "On" : "Off")} · {JumpRanges.FormatLy(_preferences.JumpRange.Ly)}";

    /// <summary>NPC kills rising, NPC kills, and each hunting profile's "likely now".</summary>
    [ObservableProperty]
    public partial IReadOnlyList<NearbySortOption> SortOptions { get; private set; } = [];

    [ObservableProperty]
    public partial NearbySortOption? SelectedSort { get; set; }

    partial void OnSelectedSortChanged(NearbySortOption? value)
    {
        if (!_syncingSort && value is not null && value.Id != EffectiveSort)
            _preferences.Nearby = _preferences.Nearby with { Sort = value.Id };
    }

    /// <summary>The systems in reach, in the server's order; empty until loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial IReadOnlyList<NearbyRowViewModel> Rows { get; private set; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Why there are no rows: no focus, nothing to search by, loading, or an error; null with rows.</summary>
    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    [ObservableProperty]
    public partial bool HasError { get; private set; }

    /// <summary>"12 systems near Tama, NPC kills rising first."</summary>
    [ObservableProperty]
    public partial string? Summary { get; private set; }

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
            return sort is Nearby.SortActivity or Nearby.SortNpcKills || ProfileIds.Contains(sort) ? sort : Nearby.SortActivity;
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
        Rows = [];
        Summary = null;
        HasError = false;
        StatusText = status;
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
            var reach = await session.GetReachAsync(request.From, request.Jumps, request.Ly, request.Profile, request.Limit, request.Sort, cancel.Token);
            var (byJumps, byLy) = (request.Jumps is not null, request.Ly is not null);
            Rows = [.. reach.Systems
                .Where(s => s.SystemId != reach.From && model.IndexOf.ContainsKey(s.SystemId))
                .Select(s => new NearbyRowViewModel(
                    s.SystemId,
                    model.Names[model.IndexOf[s.SystemId]],
                    SecurityStatus.Format(s.Security),
                    new SolidColorBrush(ThemeService.ToColor(_catalog.SecurityColor(s.Security))),
                    Nearby.DistanceText(s.Jumps, s.Ly, byJumps, byLy)))];
            var order = request.Profile is { } profile
                ? $"{_map.HuntingProfiles.FirstOrDefault(p => p.Id == profile)?.Name ?? profile} likely now first"
                : request.Sort == ReachSorts.NpcKills ? "most NPC kills first" : "NPC kills rising first";
            Summary = $"{Rows.Count} systems near {name}, {order}.";
            StatusText = null;
        }
        catch (OperationCanceledException)
        {
            // Another question, or logged out.
        }
        catch (HuntersApiException e)
        {
            _loadedFor = null;
            Rows = [];
            Summary = null;
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
public sealed record NearbyRowViewModel(int SystemId, string Name, string Security, IBrush SecurityBrush, string Distance);

/// <summary>One way to sort the Nearby rows: <see cref="Nearby.SortActivity"/>, <see cref="Nearby.SortNpcKills"/> or a profile's id.</summary>
public sealed record NearbySortOption(string Id, string Label)
{
    public override string ToString() => Label;
}

using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewEdenSoft.Hunters.Desktop.Characters;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// One system in detail, like the web's System panel (<c>SystemInfoPanel.tsx</c>): the selected system,
/// or the focused one while nothing is selected, tagged Selected and/or Focused. Its name, security and
/// region with links to zKillboard and Dotlan, Focus or Unfocus, and Destination: the main part for
/// every tracked character, ▾ for one usable character, the outcome under the buttons. Then the last
/// hour of NPC kills, ship kills, pod kills and jumps, once the server has activity.
/// </summary>
public sealed partial class SystemWidgetViewModel : WidgetViewModelBase
{
    private readonly AccountPageViewModel _account;
    private readonly MapWidgetViewModel _map;
    private readonly Destinations _destinations;

    public SystemWidgetViewModel(AccountPageViewModel account, MapWidgetViewModel map, Destinations destinations, CharacterTracking tracking)
    {
        _account = account;
        _map = map;
        _destinations = destinations;

        map.PropertyChanged += OnMapChanged;
        account.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AccountPageViewModel.Session))
                SyncTracked();
        };
        // Tracker events come from its polling threads.
        tracking.Changed += (_, _) => Dispatcher.UIThread.Post(SyncTracked);
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

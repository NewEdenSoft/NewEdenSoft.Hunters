using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Contracts.Intel;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Theming;
using NewEdenSoft.Hunters.Desktop.ViewModels.Pages;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// What is in reach of the focused system, from the server's <c>/api/intel/reach</c>, like the web's
/// Nearby panel (<c>NearbyPanel.tsx</c>). Asked for while the widget is on screen, each time the focus
/// changes. For now within 5 gate jumps, NPC kills rising first.
/// </summary>
public sealed partial class NearbyWidgetViewModel : WidgetViewModelBase
{
    /// <summary>Gate jumps searched; the Nearby settings come later.</summary>
    private const int Jumps = 5;

    /// <summary>Rows asked for: enough that 50 active systems are left once the quiet ones are hidden.</summary>
    private const int Fetch = 200;

    private readonly AccountPageViewModel _account;
    private readonly MapWidgetViewModel _map;
    private readonly ThemeCatalog _catalog;
    private CancellationTokenSource? _loading;

    /// <summary>The origin and session the rows are for, so the same question isn't asked twice.</summary>
    private (int From, HuntersSession Session)? _loadedFor;

    public NearbyWidgetViewModel(AccountPageViewModel account, MapWidgetViewModel map, ThemeCatalog catalog)
    {
        _account = account;
        _map = map;
        _catalog = catalog;
        map.PropertyChanged += OnMapChanged;
    }

    /// <summary>The systems in reach, nearest intel first; empty until loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial IReadOnlyList<NearbyRowViewModel> Rows { get; private set; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Why there are no rows: no focus, loading, or an error; null with rows.</summary>
    [ObservableProperty]
    public partial string? StatusText { get; private set; }

    [ObservableProperty]
    public partial bool HasError { get; private set; }

    /// <summary>"12 systems near Tama, NPC kills rising first."</summary>
    [ObservableProperty]
    public partial string? Summary { get; private set; }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(IsActive) && IsActive)
            Refresh();
    }

    private void OnMapChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MapWidgetViewModel.FocusedSystemId) or nameof(MapWidgetViewModel.Model))
            Refresh();
    }

    /// <summary>Asks again when the origin or the session changed, while on screen.</summary>
    private void Refresh()
    {
        var session = _account.Session;
        if (_map.Model is not { } model || session is null || _map.FocusedSystemId is not { } from || !model.IndexOf.ContainsKey(from))
        {
            _loading?.Cancel();
            _loadedFor = null;
            Rows = [];
            Summary = null;
            HasError = false;
            StatusText = "Focus a system (double click it) to see what is near it.";
            return;
        }

        if (!IsActive || _loadedFor == (from, session))
            return;

        _ = LoadAsync(session, model, from);
    }

    private async Task LoadAsync(HuntersSession session, MapModel model, int from)
    {
        _loading?.Cancel();
        using var cancel = new CancellationTokenSource();
        _loading = cancel;
        _loadedFor = (from, session);
        var name = model.Names[model.IndexOf[from]];
        if (Rows.Count == 0)
            StatusText = $"Looking around {name}…";
        HasError = false;
        try
        {
            var reach = await session.GetReachAsync(from, Jumps, null, null, Fetch, ReachSorts.NpcRising, cancel.Token);
            Rows = [.. reach.Systems
                .Where(s => s.SystemId != reach.From && model.IndexOf.ContainsKey(s.SystemId))
                .Select(s => new NearbyRowViewModel(
                    s.SystemId,
                    model.Names[model.IndexOf[s.SystemId]],
                    SecurityStatus.Format(s.Security),
                    new SolidColorBrush(ThemeService.ToColor(_catalog.SecurityColor(s.Security))),
                    s.Jumps is { } j ? $"{j} j" : "no gates"))];
            Summary = $"{Rows.Count} systems near {name}, NPC kills rising first.";
            StatusText = null;
        }
        catch (OperationCanceledException)
        {
            // Another origin, or logged out.
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

using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Client.Tracking;
using NewEdenSoft.Hunters.Contracts.Kills;
using NewEdenSoft.Hunters.Desktop.Images;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// The System widget's recent kills, like the web's (<c>RecentKills</c>): the newest <see cref="Limit"/>
/// from <c>/api/kills/systems/{id}</c>, asked for while on screen and again every minute
/// (<see cref="Refresh"/>), the rows shown kept while the next answer comes. Names come from ESI through
/// <see cref="EveNames"/> ("…" until they come), ship icons from the image server.
/// </summary>
public sealed partial class SystemKillsViewModel(EveNames names, PortraitCache images) : ObservableObject
{
    public const int Limit = 10;

    /// <summary>The system and session the kills are wanted for; null with none (no system, logged out, no kills on the server).</summary>
    private (int SystemId, HuntersSession Session)? _wanted;

    private SystemKills? _answer;
    private string? _error;
    private CancellationTokenSource? _loading;

    /// <summary>The kills, newest first; empty while loading the first answer, on an error, or with none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    public partial IReadOnlyList<KillRowViewModel> Rows { get; private set; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>"Loading…", "No kills here in the kept history.", or the error, in place of the rows; null with rows.</summary>
    [ObservableProperty]
    public partial string? Text { get; private set; }

    [ObservableProperty]
    public partial bool IsError { get; private set; }

    /// <summary>
    /// The kills for this system and session (null for none), asked for when <paramref name="load"/> (on
    /// screen) unless they are here or on their way.
    /// </summary>
    public void Show(int? systemId, HuntersSession? session, bool load)
    {
        (int, HuntersSession)? wanted = systemId is { } id && session is not null ? (id, session) : null;
        if (wanted != _wanted)
        {
            _loading?.Cancel();
            _loading = null;
            _wanted = wanted;
            _answer = null;
            _error = null;
            Render();
        }

        if (load && _answer is null && _loading is null)
            _ = LoadAsync();
    }

    /// <summary>Every minute while on screen: the same system's kills again, and the times ago written again.</summary>
    public void Refresh()
    {
        if (_wanted is not null && _loading is null)
            _ = LoadAsync();
        Render();
    }

    private async Task LoadAsync()
    {
        if (_wanted is not { } wanted)
            return;

        var cancel = new CancellationTokenSource();
        _loading = cancel;
        try
        {
            var answer = await wanted.Session.GetSystemKillsAsync(wanted.SystemId, Limit, cancel.Token);
            if (cancel.IsCancellationRequested)
                return;
            _answer = answer;
            _error = null;
            Render();
            if (await names.ResolveAsync(SystemInfo.KillNameIds(answer.Kills), cancel.Token) && !cancel.IsCancellationRequested)
                Render();
        }
        catch (OperationCanceledException)
        {
            // Another system, or logged out.
        }
        catch (HuntersApiException e)
        {
            if (cancel.IsCancellationRequested)
                return;
            // A failed refresh keeps the kills shown.
            if (_answer is null)
            {
                _error = e.Message;
                Render();
            }
        }
        finally
        {
            if (_loading == cancel)
                _loading = null;
        }
    }

    private void Render()
    {
        IsError = _error is not null;
        if (_answer is not { } answer)
        {
            Rows = [];
            Text = _wanted is null ? null : _error ?? "Loading…";
            return;
        }

        var now = DateTimeOffset.UtcNow;
        Rows = [.. answer.Kills.Select(k => new KillRowViewModel(k, names.Get, now, images))];
        Text = Rows.Count == 0 ? "No kills here in the kept history." : null;
    }
}

/// <summary>
/// One recent kill: the ship's icon, ship and pilot, then by whom (the final blow's pilot and ship, or the
/// NPC, with +N for the other attackers; the attacker count when the final blow is unknown), NPC when only
/// NPCs were on it, how long ago and the value. Opens on zKillboard.
/// </summary>
public sealed partial class KillRowViewModel : ObservableObject
{
    public KillRowViewModel(RecentKill kill, Func<long, string?> nameOf, DateTimeOffset now, PortraitCache? images = null)
    {
        Ship = kill.Victim.ShipTypeId is { } ship ? nameOf(ship) ?? "…" : "Unknown";
        Victim = SystemInfo.PartyName(kill.Victim, nameOf) ?? "";
        IsNpc = kill.Npc;
        if (kill.FinalBlow is { } finalBlow)
        {
            By = $"by {SystemInfo.PartyName(finalBlow, nameOf) ?? "…"}";
            // The final blow's ship only for a pilot: an NPC's name is its ship already.
            ByExtra = (finalBlow.CharacterId is not null && finalBlow.ShipTypeId is { } byShip && nameOf(byShip) is { } shipName ? $" · {shipName}" : "")
                + (kill.AttackerCount > 1 ? $" +{kill.AttackerCount - 1}" : "");
        }
        else
        {
            By = "";
            ByExtra = kill.AttackerCount == 1 ? "1 attacker" : $"{kill.AttackerCount} attackers";
        }

        Ago = SystemInfo.TimeAgo(kill.Time, now);
        TimeTip = $"{kill.Time.UtcDateTime:yyyy-MM-dd HH:mm} UTC";
        Value = SystemInfo.FormatIsk(kill.TotalValue);
        Url = SystemLinks.ZkillboardKill(kill.KillmailId);

        if (images is not null && kill.Victim.ShipTypeId is { } typeId)
            _ = LoadIconAsync(images.GetTypeIconAsync(typeId, 32));
    }

    public string Ship { get; }

    /// <summary>The pilot (or corporation) that lost the ship; empty when unknown.</summary>
    public string Victim { get; }

    public bool IsNpc { get; }

    /// <summary>"NPC " before By when only NPCs were on it; empty otherwise.</summary>
    public string NpcText => IsNpc ? "NPC " : "";

    /// <summary>"by Pilot", or empty when the final blow is unknown.</summary>
    public string By { get; }

    /// <summary>" · Ship +2" after By, faint; or the attacker count when the final blow is unknown.</summary>
    public string ByExtra { get; }

    public string Ago { get; }

    public string TimeTip { get; }

    /// <summary>The ISK lost: "12.5k", "3.2M", "1.05B".</summary>
    public string Value { get; }

    public Uri Url { get; }

    /// <summary>The ship's icon; null until it has loaded, or without one.</summary>
    [ObservableProperty]
    public partial Bitmap? Icon { get; private set; }

    // A cached icon is set at once (awaiting a finished task doesn't yield), so rows rebuilt every minute don't flicker.
    private async Task LoadIconAsync(Task<Bitmap?> icon) => Icon = await icon;
}

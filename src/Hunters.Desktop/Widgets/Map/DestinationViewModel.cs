using CommunityToolkit.Mvvm.ComponentModel;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Desktop.Characters;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// Setting the autopilot destination to one system, and what it came to, for a Nearby card. Kept per
/// system, so the outcome stays while the card's rows are rebuilt. A success clears after a few seconds.
/// </summary>
public sealed partial class DestinationViewModel(int systemId, string name, Destinations destinations, Func<HuntersSession?> session) : ObservableObject
{
    /// <summary>How long a success stays: it needs no keeping.</summary>
    private static readonly TimeSpan OkShown = TimeSpan.FromSeconds(4);

    public int SystemId => systemId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; private set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus), nameof(StatusText), nameof(IsOk), nameof(IsDanger))]
    public partial DestinationOutcome? Status { get; private set; }

    public bool HasStatus => Status is not null;

    public string? StatusText => Status is { } status ? string.Join(Environment.NewLine, status.Lines) : null;

    public bool IsOk => Status?.Tone == DestinationTone.Ok;

    public bool IsDanger => Status?.Tone == DestinationTone.Danger;

    public async Task SetForAsync(IReadOnlyList<DestinationCharacter> characters)
    {
        if (characters.Count == 0 || IsBusy || session() is not { } current)
            return;

        IsBusy = true;
        Status = Destinations.Setting(characters);
        var outcome = await destinations.SetAsync(current, systemId, name, characters);
        IsBusy = false;
        Status = outcome;
        if (outcome.Tone != DestinationTone.Ok)
            return;

        await Task.Delay(OkShown);
        if (ReferenceEquals(Status, outcome))
            Status = null;
    }
}

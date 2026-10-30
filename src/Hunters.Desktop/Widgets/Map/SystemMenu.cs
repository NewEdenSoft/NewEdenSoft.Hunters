using NewEdenSoft.Hunters.Desktop.Characters;

namespace NewEdenSoft.Hunters.Desktop.Widgets.Map;

/// <summary>
/// A system's menu, like the web's <c>SystemContextMenu</c>, on the map and on a Nearby card: its name and
/// security, Focus system (or Clear focus on the focused one), and Navigate to: All for every tracked
/// character, then each usable character (online first, then the main). Built when it opens.
/// </summary>
/// <param name="System">The system, for the header.</param>
/// <param name="IsFocused">It is the focused system: the menu offers Clear focus instead.</param>
/// <param name="Tracked">The tracked characters, for All.</param>
/// <param name="Usable">The account's usable characters, online first, then the main.</param>
/// <param name="IsBusy">A destination is being set there: Navigate to waits until it is done.</param>
/// <param name="Focus">Focus system.</param>
/// <param name="ClearFocus">Clear focus.</param>
/// <param name="Navigate">Sets the autopilot destination there for these characters.</param>
public sealed record SystemMenu(
    MapSystemInfo System,
    bool IsFocused,
    IReadOnlyList<DestinationCharacter> Tracked,
    IReadOnlyList<DestinationCharacter> Usable,
    bool IsBusy,
    Action Focus,
    Action ClearFocus,
    Action<IReadOnlyList<DestinationCharacter>> Navigate)
{
    public string FocusText => IsFocused ? "Clear focus" : "Focus system";

    /// <summary>"All (2)", or "All" without tracked characters.</summary>
    public string AllText => Tracked.Count > 0 ? $"All ({Tracked.Count})" : "All";

    public string AllTip => Tracked.Count == 0 ? "No tracked characters" : $"Every tracked character: {string.Join(", ", Tracked.Select(c => c.Name))}";

    public bool CanNavigateAll => Tracked.Count > 0 && !IsBusy;

    public static string CharacterTip(DestinationCharacter character) =>
        character.IsOnline ? "Online" : "Not online: the destination shows when it logs in, if at all";
}

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>
/// When the focused system may change by itself, and how the map's clicks work (Settings, "Map
/// behaviour"), like the web's <c>MapBehaviour</c> in <c>map/autoFocus.ts</c>.
/// </summary>
/// <param name="EnterKnownSpace">Focus a character's system when it comes from unknown space (wormhole space, or anywhere not on the map) into known space.</param>
/// <param name="LongMove">Focus a character's system after a move of at least <paramref name="MinJumps"/> gate jumps (a filament, a jump bridge, a cyno).</param>
/// <param name="DoubleClickFocus">Double click a system to focus it. A single click selects it either way.</param>
public sealed record MapBehaviour(bool EnterKnownSpace = true, bool LongMove = true, int MinJumps = 5, bool DoubleClickFocus = true)
{
    /// <summary>The minimum jumps offered for a long move.</summary>
    public static readonly IReadOnlyList<int> MinJumpChoices = [2, 3, 5, 10, 20];
}

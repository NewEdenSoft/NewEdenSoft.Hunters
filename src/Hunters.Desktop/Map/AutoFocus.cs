using System.Runtime.CompilerServices;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>When the focused system may change by itself, and how the map's clicks work (Settings, "Map behaviour").</summary>
/// <param name="EnterKnownSpace">Focus a character's system when it comes from unknown space (wormhole space, or anywhere not on the map) into known space.</param>
/// <param name="LongMove">Focus a character's system after a move of at least <paramref name="MinJumps"/> gate jumps (a filament, a jump bridge, a cyno).</param>
/// <param name="DoubleClickFocus">Double click a system to focus it. A single click selects it either way.</param>
public sealed record MapBehaviour(bool EnterKnownSpace = true, bool LongMove = true, int MinJumps = 5, bool DoubleClickFocus = true)
{
    /// <summary>The minimum jumps offered for a long move.</summary>
    public static readonly IReadOnlyList<int> MinJumpChoices = [2, 3, 5, 10, 20];
}

/// <summary>Why a move focuses its new system.</summary>
public enum AutoFocusReason
{
    KnownSpace,
    LongMove,
}

/// <summary>
/// Whether a character's move should focus its new system, like the web's <c>map/autoFocus.ts</c>.
/// </summary>
public static class AutoFocus
{
    private static readonly ConditionalWeakTable<MapModel, int[][]> Neighbours = new();

    /// <summary>
    /// Whether a character's move from one system to another should focus the new one. Coming from a
    /// system that isn't on the map (unknown space) into one that is: <see cref="AutoFocusReason.KnownSpace"/>.
    /// Between two systems on the map, at least <see cref="MapBehaviour.MinJumps"/> gate jumps apart (or
    /// with no gate route at all): <see cref="AutoFocusReason.LongMove"/>. A first known location (no
    /// previous system) never focuses.
    /// </summary>
    public static AutoFocusReason? Reason(MapModel model, int? from, int to, MapBehaviour behaviour)
    {
        if (from is not { } fromId || fromId == to || !model.IndexOf.TryGetValue(to, out var target))
            return null;
        if (!model.IndexOf.TryGetValue(fromId, out var origin))
            return behaviour.EnterKnownSpace ? AutoFocusReason.KnownSpace : null;
        if (!behaviour.LongMove)
            return null;
        return JumpsWithin(model, origin, target, behaviour.MinJumps - 1) ? null : AutoFocusReason.LongMove;
    }

    /// <summary>Whether <paramref name="to"/> is at most <paramref name="limit"/> gate jumps from <paramref name="from"/> (a breadth-first search that stops at the limit).</summary>
    public static bool JumpsWithin(MapModel model, int from, int to, int limit)
    {
        if (from == to)
            return true;

        var adjacent = Neighbours.GetValue(model, BuildNeighbours);
        var seen = new HashSet<int> { from };
        var frontier = new List<int> { from };
        for (var depth = 1; depth <= limit && frontier.Count > 0; depth++)
        {
            var next = new List<int>();
            foreach (var system in frontier)
            {
                foreach (var n in adjacent[system])
                {
                    if (n == to)
                        return true;
                    if (seen.Add(n))
                        next.Add(n);
                }
            }

            frontier = next;
        }

        return false;
    }

    /// <summary>Each system's gate neighbours (by index), built once per model.</summary>
    private static int[][] BuildNeighbours(MapModel model)
    {
        var lists = Enumerable.Range(0, model.Count).Select(_ => new List<int>()).ToArray();
        for (var g = 0; g < model.Gates.Length; g += 2)
        {
            lists[model.Gates[g]].Add(model.Gates[g + 1]);
            lists[model.Gates[g + 1]].Add(model.Gates[g]);
        }

        return [.. lists.Select(l => l.ToArray())];
    }
}

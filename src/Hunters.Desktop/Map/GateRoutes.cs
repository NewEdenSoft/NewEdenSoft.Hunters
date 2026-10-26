using System.Runtime.CompilerServices;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>Routes over the stargates, like the web's <c>map/route.ts</c>.</summary>
public static class GateRoutes
{
    private static readonly ConditionalWeakTable<MapModel, int[][]> Neighbours = new();

    /// <summary>Each system's gate neighbours (by index), built once per model.</summary>
    public static int[][] NeighboursOf(MapModel model) => Neighbours.GetValue(model, Build);

    /// <summary>
    /// The shortest gate route from <paramref name="from"/> to <paramref name="to"/> (system indexes, both
    /// ends included; a breadth-first search), or null when no gates lead there. Shortest by jumps, like
    /// the server's reach, not safest.
    /// </summary>
    public static int[]? Route(MapModel model, int from, int to)
    {
        if (from == to)
            return [from];

        var adjacent = NeighboursOf(model);
        var previous = new int[model.Count];
        Array.Fill(previous, -1);
        previous[from] = from;
        var frontier = new List<int> { from };
        while (frontier.Count > 0)
        {
            var next = new List<int>();
            foreach (var system in frontier)
            {
                foreach (var n in adjacent[system])
                {
                    if (previous[n] >= 0)
                        continue;
                    previous[n] = system;
                    if (n == to)
                    {
                        var route = new List<int> { to };
                        for (var at = to; at != from; at = previous[at])
                            route.Add(previous[at]);
                        route.Reverse();
                        return [.. route];
                    }

                    next.Add(n);
                }
            }

            frontier = next;
        }

        return null;
    }

    private static int[][] Build(MapModel model)
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

using System.Collections.Frozen;

namespace NewEdenSoft.Hunters.Domain.Map;

/// <summary>A system for <see cref="MapGeometry"/>: its position in light years, in 3D.</summary>
public readonly record struct GeometrySystem(int Id, int RegionId, double Security, double X, double Y, double Z);

/// <summary>
/// The map as arrays for fast searches: systems by index, the stargate graph in compressed rows (each
/// system's neighbours are <c>Neighbours[Offsets[i]..Offsets[i + 1]]</c>), 3D positions in light years,
/// and whether a jump drive can land. Immutable; built once per SDE build.
/// </summary>
public sealed class MapGeometry
{
    private MapGeometry(GeometrySystem[] systems, int[] offsets, int[] neighbours)
    {
        Systems = systems;
        Offsets = offsets;
        Neighbours = neighbours;
        IndexOf = systems.Select((s, i) => (s.Id, i)).ToFrozenDictionary(p => p.Id, p => p.i);
        CynoTarget = [.. systems.Select(s => CynoTargets.IsTarget(s.Id, s.RegionId, s.Security))];
    }

    public IReadOnlyList<GeometrySystem> Systems { get; }

    public FrozenDictionary<int, int> IndexOf { get; }

    public IReadOnlyList<bool> CynoTarget { get; }

    internal int[] Offsets { get; }

    internal int[] Neighbours { get; }

    public int Count => Systems.Count;

    /// <summary>
    /// Builds the geometry. Gates may be listed once or in both directions; gates to systems not in
    /// <paramref name="systems"/> are ignored.
    /// </summary>
    public static MapGeometry Build(IEnumerable<GeometrySystem> systems, IEnumerable<(int From, int To)> gates)
    {
        var list = systems.OrderBy(s => s.Id).ToArray();
        var index = list.Select((s, i) => (s.Id, i)).ToDictionary(p => p.Id, p => p.i);

        var adjacent = list.Select(_ => new SortedSet<int>()).ToArray();
        foreach (var (from, to) in gates)
        {
            if (from != to && index.TryGetValue(from, out var a) && index.TryGetValue(to, out var b))
            {
                adjacent[a].Add(b);
                adjacent[b].Add(a);
            }
        }

        var offsets = new int[list.Length + 1];
        for (var i = 0; i < list.Length; i++)
            offsets[i + 1] = offsets[i] + adjacent[i].Count;
        var neighbours = adjacent.SelectMany(n => n).ToArray();
        return new MapGeometry(list, offsets, neighbours);
    }

    /// <summary>Straight-line distance between two systems, in light years.</summary>
    public double DistanceLy(int a, int b)
    {
        var (s, t) = (Systems[a], Systems[b]);
        return Math.Sqrt((s.X - t.X) * (s.X - t.X) + (s.Y - t.Y) * (s.Y - t.Y) + (s.Z - t.Z) * (s.Z - t.Z));
    }

    /// <summary>Gate jumps from <paramref name="origin"/> to every system (breadth first); -1 where no gates lead.</summary>
    public int[] JumpsFrom(int origin)
    {
        var jumps = new int[Count];
        Array.Fill(jumps, -1);
        var queue = new int[Count];
        int head = 0, tail = 0;
        jumps[origin] = 0;
        queue[tail++] = origin;
        while (head < tail)
        {
            var current = queue[head++];
            for (var n = Offsets[current]; n < Offsets[current + 1]; n++)
            {
                var next = Neighbours[n];
                if (jumps[next] < 0)
                {
                    jumps[next] = jumps[current] + 1;
                    queue[tail++] = next;
                }
            }
        }

        return jumps;
    }
}

namespace NewEdenSoft.Hunters.Domain.Map;

/// <summary>A system in reach, with both distances from the origin.</summary>
/// <param name="Index">The system's index in the <see cref="MapGeometry"/>.</param>
/// <param name="Jumps">Gate jumps from the origin; null when no gates lead there.</param>
/// <param name="Ly">Straight-line distance from the origin in light years.</param>
public readonly record struct ReachedSystem(int Index, int? Jumps, double Ly, bool CynoTarget);

/// <summary>
/// The systems in reach of an origin (docs/map-and-activity.md, "Reach"). The user's limits are the
/// query: within <c>maxJumps</c> gate jumps (whatever the light-year distance), and/or every cyno target
/// within <c>maxLy</c> light years (however many gate jumps away). The result is the union, and every
/// system carries both distances, so the client decides what "in jump range" means for its ship.
/// </summary>
public static class Reach
{
    public const int MinJumps = 1, MaxJumps = 50;
    public const double MinLy = 0.1, MaxLy = 20;

    /// <summary>Systems in reach, the origin first (0 jumps, 0 ly), the rest by gate jumps then light years.</summary>
    /// <exception cref="ArgumentException">Neither limit given, or one out of range.</exception>
    public static List<ReachedSystem> Find(MapGeometry map, int origin, int? maxJumps, double? maxLy)
    {
        if (maxJumps is null && maxLy is null)
            throw new ArgumentException("Give a number of gate jumps, a light-year range, or both.");
        if (maxJumps is < MinJumps or > MaxJumps)
            throw new ArgumentOutOfRangeException(nameof(maxJumps), $"Gate jumps must be {MinJumps}–{MaxJumps}.");
        if (maxLy is < MinLy or > MaxLy)
            throw new ArgumentOutOfRangeException(nameof(maxLy), $"Light years must be {MinLy}–{MaxLy}.");

        // A search of the whole map is about 0.1 ms, so both distances are always known.
        var jumps = map.JumpsFrom(origin);
        var result = new List<ReachedSystem>();
        for (var i = 0; i < map.Count; i++)
        {
            var ly = map.DistanceLy(origin, i);
            var byGate = maxJumps is { } j && jumps[i] >= 0 && jumps[i] <= j;
            var byJumpDrive = maxLy is { } l && map.CynoTarget[i] && ly <= l;
            if (i == origin || byGate || byJumpDrive)
                result.Add(new ReachedSystem(i, jumps[i] >= 0 ? jumps[i] : null, ly, map.CynoTarget[i]));
        }

        return [.. result.OrderBy(r => r.Index != origin).ThenBy(r => r.Jumps ?? int.MaxValue).ThenBy(r => r.Ly)];
    }
}

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>
/// A uniform grid over interleaved x, y points, for finding the point nearest to the cursor without
/// scanning every system.
/// </summary>
public sealed class GridIndex
{
    private readonly Dictionary<long, List<int>> _cells = [];
    private readonly float[] _xy;
    private readonly float _cellSize;

    public GridIndex(float[] xy, float cellSize)
    {
        _xy = xy;
        _cellSize = cellSize;
        for (var i = 0; i < xy.Length / 2; i++)
        {
            var key = Key(Cell(xy[i * 2]), Cell(xy[i * 2 + 1]));
            if (!_cells.TryGetValue(key, out var list))
                _cells[key] = list = [];
            list.Add(i);
        }
    }

    /// <summary>Index of the point nearest to (x, y) within <paramref name="maxDistance"/>, or -1.</summary>
    public int Nearest(float x, float y, float maxDistance, Func<int, bool>? include = null)
    {
        var reach = (int)MathF.Ceiling(maxDistance / _cellSize);
        var cx = Cell(x);
        var cy = Cell(y);
        var best = -1;
        var bestDistance = maxDistance * maxDistance;
        for (var gx = cx - reach; gx <= cx + reach; gx++)
        {
            for (var gy = cy - reach; gy <= cy + reach; gy++)
            {
                if (!_cells.TryGetValue(Key(gx, gy), out var list))
                    continue;
                foreach (var i in list)
                {
                    var dx = _xy[i * 2] - x;
                    var dy = _xy[i * 2 + 1] - y;
                    var distance = dx * dx + dy * dy;
                    if (distance <= bestDistance && (include is null || include(i)))
                    {
                        bestDistance = distance;
                        best = i;
                    }
                }
            }
        }

        return best;
    }

    private int Cell(float value) => (int)MathF.Floor(value / _cellSize);

    private static long Key(int gx, int gy) => ((long)gx << 32) | (uint)gy;
}

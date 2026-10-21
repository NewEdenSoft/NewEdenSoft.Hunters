using System.Text.Json.Serialization;
using NewEdenSoft.Hunters.Contracts.Map;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>Which 2D layout to draw: CCP's flattened 2D map, or the 3D map seen from above.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MapLayout>))]
public enum MapLayout
{
    Flat,
    TopDown,
}

public readonly record struct MapBounds(float MinX, float MinY, float MaxX, float MaxY);

/// <summary>Where to put a region's name per layout; null when the region is too scattered for one label to mean anything.</summary>
public sealed record MapRegionLabel(int Id, string Name, (float X, float Y)? FlatCentre, (float X, float Y)? TopDownCentre)
{
    public (float X, float Y)? Centre(MapLayout layout) => layout == MapLayout.Flat ? FlatCentre : TopDownCentre;
}

/// <summary>
/// The map pack as flat arrays, indexed by system index (0..Count-1). Built once per pack; the
/// renderer and hit-testing never touch per-system objects. Matches <c>web/hunters-web/src/map/model.ts</c>.
/// </summary>
public sealed class MapModel
{
    /// <summary>A region is labelled only if its systems spread at most this many times the median region's spread.</summary>
    private const float MaxRegionSpread = 2;

    private readonly float[] _flat;
    private readonly float[] _topDown;

    private MapModel(MapPack pack)
    {
        SdeBuild = pack.SdeBuild;
        Count = pack.Systems.Count;
        Ids = new int[Count];
        Names = new string[Count];
        RegionIds = new int[Count];
        ConstellationIds = new int[Count];
        Security = new double[Count];
        _flat = new float[Count * 2];
        _topDown = new float[Count * 2];
        var indexOf = new Dictionary<int, int>(Count);

        for (var i = 0; i < Count; i++)
        {
            var s = pack.Systems[i];
            Ids[i] = s.Id;
            Names[i] = s.Name;
            RegionIds[i] = s.RegionId;
            ConstellationIds[i] = s.ConstellationId;
            Security[i] = s.Security;
            _flat[i * 2] = (float)s.Flat.X;
            _flat[i * 2 + 1] = (float)s.Flat.Y;
            _topDown[i * 2] = (float)s.TopDown.X;
            _topDown[i * 2 + 1] = (float)s.TopDown.Y;
            indexOf[s.Id] = i;
        }

        var gates = new List<int>(pack.Gates.Count * 2);
        foreach (var gate in pack.Gates)
        {
            if (indexOf.TryGetValue(gate.From, out var from) && indexOf.TryGetValue(gate.To, out var to))
            {
                gates.Add(from);
                gates.Add(to);
            }
        }

        Gates = [.. gates];
        IndexOf = indexOf;
        RegionNames = pack.Regions.ToDictionary(r => r.Id, r => r.Name);
        FlatBounds = BoundsOf(_flat);
        TopDownBounds = BoundsOf(_topDown);
        Regions = RegionLabels(pack);
    }

    public long SdeBuild { get; }

    public int Count { get; }

    public int[] Ids { get; }

    public string[] Names { get; }

    public int[] RegionIds { get; }

    public int[] ConstellationIds { get; }

    /// <summary>True security status, unrounded.</summary>
    public double[] Security { get; }

    /// <summary>Gate endpoints as pairs of system indexes.</summary>
    public int[] Gates { get; }

    public IReadOnlyDictionary<int, int> IndexOf { get; }

    public IReadOnlyDictionary<int, string> RegionNames { get; }

    /// <summary>Regions for labels, largest (most systems) first.</summary>
    public IReadOnlyList<MapRegionLabel> Regions { get; }

    public MapBounds FlatBounds { get; }

    public MapBounds TopDownBounds { get; }

    public static MapModel Build(MapPack pack) => new(pack);

    /// <summary>Interleaved x, y in light years.</summary>
    public float[] Positions(MapLayout layout) => layout == MapLayout.Flat ? _flat : _topDown;

    public MapBounds Bounds(MapLayout layout) => layout == MapLayout.Flat ? FlatBounds : TopDownBounds;

    public string RegionName(int index) => RegionNames.GetValueOrDefault(RegionIds[index], "");

    private List<MapRegionLabel> RegionLabels(MapPack pack)
    {
        var members = new Dictionary<int, List<int>>();
        for (var i = 0; i < Count; i++)
        {
            if (!members.TryGetValue(RegionIds[i], out var list))
                members[RegionIds[i]] = list = [];
            list.Add(i);
        }

        var regions = pack.Regions.Where(r => members.ContainsKey(r.Id)).ToList();
        var flat = LabelCentres(regions, members, _flat);
        var topDown = LabelCentres(regions, members, _topDown);

        return regions
            .Select((r, i) => new MapRegionLabel(r.Id, r.Name, flat[i], topDown[i]))
            .OrderByDescending(r => members[r.Id].Count)
            .ToList();
    }

    /// <summary>Mean position of each region's systems, or null for regions spread much wider than the median region.</summary>
    private static (float X, float Y)?[] LabelCentres(List<MapRegion> regions, Dictionary<int, List<int>> members, float[] xy)
    {
        var stats = regions.Select(r =>
        {
            var systems = members[r.Id];
            float x = 0, y = 0;
            foreach (var i in systems)
            {
                x += xy[i * 2];
                y += xy[i * 2 + 1];
            }

            x /= systems.Count;
            y /= systems.Count;
            float squares = 0;
            foreach (var i in systems)
                squares += (xy[i * 2] - x) * (xy[i * 2] - x) + (xy[i * 2 + 1] - y) * (xy[i * 2 + 1] - y);
            return (X: x, Y: y, Spread: MathF.Sqrt(squares / systems.Count));
        }).ToList();

        var spreads = stats.Select(s => s.Spread).Order().ToList();
        var median = spreads.Count == 0 ? 0 : spreads[spreads.Count / 2];
        return stats.Select(s => s.Spread <= median * MaxRegionSpread ? ((float, float)?)(s.X, s.Y) : null).ToArray();
    }

    private static MapBounds BoundsOf(float[] xy)
    {
        if (xy.Length == 0)
            return default;

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (var i = 0; i < xy.Length; i += 2)
        {
            minX = Math.Min(minX, xy[i]);
            maxX = Math.Max(maxX, xy[i]);
            minY = Math.Min(minY, xy[i + 1]);
            maxY = Math.Max(maxY, xy[i + 1]);
        }

        return new MapBounds(minX, minY, maxX, maxY);
    }
}

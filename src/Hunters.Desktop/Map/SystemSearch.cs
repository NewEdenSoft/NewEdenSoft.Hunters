using System.Runtime.CompilerServices;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>
/// Finds systems by name, ranked as the web map ranks them (<c>web/hunters-web/src/map/systemSearch.ts</c>):
/// the exact name first, then names that start with the query, then names that contain it;
/// alphabetical within each. Case and punctuation are ignored, so <c>1dq1a</c> finds <c>1DQ1-A</c>.
/// </summary>
public static class SystemSearch
{
    private static readonly ConditionalWeakTable<MapModel, string[]> Keys = new();

    /// <summary>Lower case, letters and digits only.</summary>
    public static string Normalize(string name) =>
        string.Concat(name.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant));

    /// <summary>The matching systems as system indexes, best first.</summary>
    public static IReadOnlyList<int> Find(MapModel model, string query, int limit = 8)
    {
        var q = Normalize(query);
        if (q.Length == 0)
            return [];

        var names = Keys.GetValue(model, m => m.Names.Select(Normalize).ToArray());
        var ranked = new List<(int Index, int Rank)>();
        for (var i = 0; i < names.Length; i++)
        {
            var at = names[i].IndexOf(q, StringComparison.Ordinal);
            if (at >= 0)
                ranked.Add((i, names[i].Length == q.Length ? 0 : at == 0 ? 1 : 2));
        }

        return ranked
            .OrderBy(r => r.Rank)
            .ThenBy(r => model.Names[r.Index], StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(r => r.Index)
            .ToList();
    }
}

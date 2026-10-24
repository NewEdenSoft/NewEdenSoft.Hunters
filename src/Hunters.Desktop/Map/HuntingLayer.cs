using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Desktop.Theming;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>A shown profile's badge at a system; faint when seen there, but not usually at this hour.</summary>
/// <param name="Icon">An SVG path (24 x 24) drawn instead of the code.</param>
public sealed record HuntingBadge(string Profile, string Code, Rgba Colour, string? Icon, bool Faint);

/// <summary>The badges of one system, drawn in a row below its name.</summary>
public sealed record HuntingBadgeGroup(int SystemIndex, IReadOnlyList<HuntingBadge> Badges);

/// <summary>A shown profile seen at a system, for the hover panel.</summary>
/// <param name="PeakNow">This hour against the busiest hour of the day there, 0–1.</param>
public sealed record PlaceProfile(string Profile, string Name, ProfileStyle Style, double Score, double Likely, double PeakNow)
{
    /// <summary>"peak now", "now" (usually seen here at this hour, UTC), or "seen".</summary>
    public string When => Likely > 0 ? PeakNow >= 0.8 ? "peak now" : "now" : "seen";

    public bool IsLikely => Likely > 0;
}

/// <summary>
/// The hunting profiles on the map: a glow per system (0–1, the strongest shown profile's "likely now"
/// there, relative to that profile's strongest system) and badges for the shown profiles seen there.
/// Matches <c>web/hunters-web/src/map/hunting.ts</c>.
/// </summary>
public sealed record HuntingLayer(float[] Glow, IReadOnlyList<HuntingBadgeGroup> Badges)
{
    /// <summary>
    /// "Likely now": the score times this hour's share of the profile's sightings in the system. High where
    /// the habit is strong and this is its usual hour.
    /// </summary>
    public static double LikelyNow(double score, IReadOnlyList<double> hours, int hour)
    {
        var total = hours.Sum();
        return total > 0 && hour < hours.Count ? score * hours[hour] / total : 0;
    }

    /// <summary>
    /// Builds the layer for the shown profiles (in the given order) at the given hour of the day (UTC).
    /// Systems not on this map are skipped. Each profile's glow is relative to its own strongest system
    /// now, so a rare target still lights up where it is most likely; the square root lifts the middle values.
    /// </summary>
    public static HuntingLayer Build(MapModel model, HuntingOverview overview, IReadOnlyList<string> shown, int hour, Func<string, ProfileStyle> styleOf)
    {
        var glow = new float[model.Count];
        var badges = new Dictionary<int, List<HuntingBadge>>();

        foreach (var profile in shown)
        {
            if (overview.Profiles.FirstOrDefault(p => p.Profile == profile) is not { } systems)
                continue;

            var style = styleOf(profile);
            // Only systems on this map count, also for the strongest.
            var indexes = systems.SystemIds.Select(id => model.IndexOf.TryGetValue(id, out var i) ? i : -1).ToArray();
            var likely = indexes.Select((index, i) => index < 0 ? 0 : LikelyNow(systems.Score[i], systems.Hours[i], hour)).ToArray();
            var strongest = likely.DefaultIfEmpty(0).Max();

            for (var i = 0; i < indexes.Length; i++)
            {
                var index = indexes[i];
                if (index < 0)
                    continue;
                if (strongest > 0 && likely[i] > 0)
                    glow[index] = Math.Max(glow[index], (float)Math.Sqrt(likely[i] / strongest));
                if (!badges.TryGetValue(index, out var row))
                    badges[index] = row = [];
                row.Add(new HuntingBadge(profile, style.Code, style.Colour, ProfileIcons.PathOf(style.Icon), likely[i] <= 0));
            }
        }

        return new HuntingLayer(glow, badges.Select(b => new HuntingBadgeGroup(b.Key, b.Value)).ToList());
    }

    /// <summary>
    /// The shown profiles seen at each system (by system index), for the hover panel: the ones likely now
    /// first, then by score.
    /// </summary>
    public static Dictionary<int, List<PlaceProfile>> ProfilesBySystem(
        MapModel model, HuntingOverview overview, IReadOnlyList<string> shown, int hour, Func<string, ProfileStyle> styleOf, Func<string, string> nameOf)
    {
        var bySystem = new Dictionary<int, List<PlaceProfile>>();
        foreach (var systems in overview.Profiles)
        {
            if (!shown.Contains(systems.Profile))
                continue;

            var style = styleOf(systems.Profile);
            for (var i = 0; i < systems.SystemIds.Count; i++)
            {
                if (!model.IndexOf.TryGetValue(systems.SystemIds[i], out var index))
                    continue;

                var hours = systems.Hours[i];
                var busiest = hours.DefaultIfEmpty(0).Max();
                if (!bySystem.TryGetValue(index, out var list))
                    bySystem[index] = list = [];
                list.Add(new PlaceProfile(
                    systems.Profile,
                    nameOf(systems.Profile),
                    style,
                    systems.Score[i],
                    LikelyNow(systems.Score[i], hours, hour),
                    busiest > 0 && hour < hours.Count ? hours[hour] / busiest : 0));
            }
        }

        foreach (var list in bySystem.Values)
            list.Sort((a, b) => b.Likely != a.Likely ? b.Likely.CompareTo(a.Likely) : b.Score.CompareTo(a.Score));
        return bySystem;
    }
}

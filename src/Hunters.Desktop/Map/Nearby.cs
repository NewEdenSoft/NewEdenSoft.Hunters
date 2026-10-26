using System.Globalization;
using NewEdenSoft.Hunters.Contracts.Intel;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>What the Nearby widget looks for around the focused system, saved with the desktop settings.</summary>
/// <param name="Jumps">List only systems within this many gate jumps (cut here, not asked for); null for no limit.</param>
/// <param name="ByJumpRange">Show the light years and mark the cyno targets within the Jump range widget's range.</param>
/// <param name="Sort"><see cref="Nearby.SortActivity"/> (the rise in NPC kills), <see cref="Nearby.SortNpcKills"/> (the most), <see cref="Nearby.SortTarget"/> (the chance of a target), or a hunting profile's id (its "likely now").</param>
/// <param name="ShowQuiet">Show the systems without any activity too (hidden by default).</param>
public sealed record NearbySetting(int? Jumps = 5, bool ByJumpRange = false, string Sort = Nearby.SortActivity, bool ShowQuiet = false);

/// <summary>A reach query (<c>/api/intel/reach</c>) from the focused system: the cyno targets within <paramref name="Ly"/>.</summary>
/// <param name="Profile">Sort by this profile's "likely now"; null sorts by <paramref name="Sort"/>.</param>
public sealed record NearbyRequest(int From, double Ly, string? Profile, string Sort, int Limit);

/// <summary>The Nearby widget's rules, like the web's <c>map/nearby.ts</c>.</summary>
public static class Nearby
{
    public const string SortActivity = "activity";

    public const string SortNpcKills = "npc-kills";

    /// <summary>The sort by <see cref="TargetChance"/>, done here rather than by the server.</summary>
    public const string SortTarget = "target";

    /// <summary>NPC kills an hour that make about a 63% chance of someone ratting (1 - 1/e): one ratter, roughly.</summary>
    private const double NpcKillsPerRatter = 40;

    /// <summary>The gate jumps offered.</summary>
    public static readonly IReadOnlyList<int> JumpChoices = [3, 5, 10, 15];

    /// <summary>Cards shown: more is a long list to read in a widget.</summary>
    public const int Limit = 50;

    /// <summary>Rows asked for: enough that 50 active systems are left once the quiet ones are hidden.</summary>
    public const int Fetch = 200;

    /// <summary>A saved setting, with the defaults for anything missing or out of range.</summary>
    public static NearbySetting Validate(NearbySetting? saved)
    {
        var defaults = new NearbySetting();
        if (saved is null)
            return defaults;
        return saved with
        {
            Jumps = saved.Jumps is null || JumpChoices.Contains(saved.Jumps.Value) ? saved.Jumps : defaults.Jumps,
            Sort = string.IsNullOrEmpty(saved.Sort) ? defaults.Sort : saved.Sort,
        };
    }

    /// <summary>
    /// The reach query for the setting, from the focused system: null without a focus, or with neither gate
    /// jumps nor jump range chosen. It always asks for the cyno targets within the Jump range widget's light
    /// years, never by gate jumps: those are cut here (<see cref="WithinJumps"/>), so changing them asks
    /// nothing new. A sort by a profile the server doesn't list falls back to activity.
    /// </summary>
    public static NearbyRequest? Request(NearbySetting setting, int? from, double rangeLy, IReadOnlyCollection<string> profiles)
    {
        if (from is not { } origin || (setting.Jumps is null && !setting.ByJumpRange))
            return null;
        return new NearbyRequest(
            origin,
            rangeLy,
            profiles.Contains(setting.Sort) ? setting.Sort : null,
            NpcSort(setting.Sort),
            Fetch);
    }

    /// <summary>
    /// The server's order when not sorting by a profile: the most NPC kills, or else their rise. The target
    /// chance is sorted here, from the busiest systems the server sends.
    /// </summary>
    public static string NpcSort(string sort) => sort is SortNpcKills or SortTarget ? ReachSorts.NpcKills : ReachSorts.NpcRising;

    /// <summary>
    /// The chance (0–1) of a target in the system now: someone ratting (NPC kills in the last hour), or a
    /// hunting profile seen at this hour (the sum of their "likely now", the sightings expected this hour),
    /// each as the chance of at least one, taken as independent.
    /// </summary>
    public static double TargetChance(ReachActivity? activity, IEnumerable<ReachProfile> profiles)
    {
        var seen = 1 - Math.Exp(-profiles.Sum(p => p.LikelyNow));
        var ratting = 1 - Math.Exp(-(activity?.NpcKills.LastHour ?? 0) / NpcKillsPerRatter);
        return 1 - (1 - seen) * (1 - ratting);
    }

    /// <summary>Highest target chance first, then nearest (gate jumps, then light years); ties keep their order.</summary>
    public static IEnumerable<T> ByTargetChance<T>(IEnumerable<T> rows, Func<T, (double Chance, int? Jumps, double Ly)> of) =>
        rows
            .Select(row => (Row: row, Key: of(row)))
            .OrderByDescending(r => r.Key.Chance)
            .ThenBy(r => r.Key.Jumps ?? int.MaxValue)
            .ThenBy(r => r.Key.Ly)
            .Select(r => r.Row);

    /// <summary>
    /// Something happens here: NPC, ship or pod kills in the last hour or the last 24 hours, or a hunting
    /// profile seen. Jumps alone don't count: traffic passing through isn't a target. Without activity data
    /// (the server has none yet) a system can't be judged, so it counts as active.
    /// </summary>
    public static bool IsActive(ReachActivity? activity, int profiles)
    {
        if (profiles > 0 || activity is null)
            return true;
        return new[] { activity.NpcKills, activity.ShipKills, activity.PodKills }.Any(m => m.LastHour > 0 || m.Day > 0);
    }

    /// <summary>Within the gate jumps chosen (any row without them). The server isn't asked by gate jumps; the list is cut here.</summary>
    public static bool WithinJumps(int? rowJumps, int? jumps) => jumps is null || (rowJumps is { } n && n <= jumps);

    /// <summary>How far a row is: "3 j", "4.2 ly", or both when both were asked for. Null jumps: no gate route.</summary>
    public static string DistanceText(int? jumps, double ly, bool byJumps, bool byLy)
    {
        var j = jumps is { } n ? $"{n} j" : "no gates";
        var l = (ly < 10 ? ly.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(ly, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)) + " ly";
        if (byJumps && byLy)
            return $"{j} · {l}";
        return byLy ? l : j;
    }

    /// <summary>Within the jump drive's reach: a cyno target within the light years (when they were asked for).</summary>
    public static bool InJumpRange(bool cynoTarget, double ly, double? range) => range is { } r && cynoTarget && ly <= r;

    /// <summary>The distance's tooltip while the light years are shown: in or out of jump range, or no cyno; else null.</summary>
    public static string? DistanceTip(bool cynoTarget, double ly, double? range) =>
        range is null ? null
        : InJumpRange(cynoTarget, ly, range) ? "In jump range"
        : cynoTarget ? "Out of jump range"
        : "No cyno: high-sec, Pochven or Zarzakh";

    /// <summary>NPC kills in the last hour against the hour before; null when the hour before is missing.</summary>
    public static int? NpcChange(ReachMetric npcKills) => npcKills.PreviousHour is { } before ? npcKills.LastHour - before : null;

    /// <summary>The profiles seen, the ones likely now first, then the rest in the server's order (by score).</summary>
    public static IEnumerable<ReachProfile> SeenFirst(IEnumerable<ReachProfile> profiles) => profiles.OrderByDescending(p => p.LikelyNow > 0);

    /// <summary>A badge's word: "peak now", "usually here now" or "seen here".</summary>
    public static string ProfileWhen(ReachProfile profile) =>
        profile.LikelyNow <= 0 ? "seen here" : (profile.PeakNow ?? 0) >= 0.8 ? "peak now" : "usually here now";
}

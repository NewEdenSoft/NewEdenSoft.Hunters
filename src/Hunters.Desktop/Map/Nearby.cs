using System.Globalization;
using NewEdenSoft.Hunters.Contracts.Intel;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>What the Nearby widget looks for around the focused system, saved with the desktop settings.</summary>
/// <param name="Jumps">Systems within this many gate jumps; null for none by gates.</param>
/// <param name="ByJumpRange">Cyno targets within the Jump range widget's light years too.</param>
/// <param name="Sort"><see cref="Nearby.SortActivity"/> (the rise in NPC kills), <see cref="Nearby.SortNpcKills"/> (the most), or a hunting profile's id (its "likely now").</param>
/// <param name="ShowQuiet">Show the systems without any activity too (hidden by default).</param>
public sealed record NearbySetting(int? Jumps = 5, bool ByJumpRange = false, string Sort = Nearby.SortActivity, bool ShowQuiet = false);

/// <summary>A reach query (<c>/api/intel/reach</c>) from the focused system.</summary>
/// <param name="Profile">Sort by this profile's "likely now"; null sorts by <paramref name="Sort"/>.</param>
public sealed record NearbyRequest(int From, int? Jumps, double? Ly, string? Profile, string Sort, int Limit);

/// <summary>The Nearby widget's rules, like the web's <c>map/nearby.ts</c>.</summary>
public static class Nearby
{
    public const string SortActivity = "activity";

    public const string SortNpcKills = "npc-kills";

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
    /// jumps nor jump range chosen. A sort by a profile the server doesn't list falls back to activity.
    /// </summary>
    public static NearbyRequest? Request(NearbySetting setting, int? from, double rangeLy, IReadOnlyCollection<string> profiles)
    {
        if (from is not { } origin || (setting.Jumps is null && !setting.ByJumpRange))
            return null;
        return new NearbyRequest(
            origin,
            setting.Jumps,
            setting.ByJumpRange ? rangeLy : null,
            profiles.Contains(setting.Sort) ? setting.Sort : null,
            NpcSort(setting.Sort),
            Fetch);
    }

    /// <summary>The server's order when not sorting by a profile: the most NPC kills, or else their rise.</summary>
    public static string NpcSort(string sort) => sort == SortNpcKills ? ReachSorts.NpcKills : ReachSorts.NpcRising;

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
}

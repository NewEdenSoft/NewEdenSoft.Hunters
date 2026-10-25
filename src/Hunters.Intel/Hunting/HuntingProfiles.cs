using System.Collections.Frozen;
using System.Globalization;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Sde;

namespace NewEdenSoft.Hunters.Intel.Hunting;

/// <summary>
/// What a rule looks for on a killmail. What dies in a system is what lives there; a ship that only
/// helped kill something may have come for that kill and left, so ship and module rules look at the
/// victim only.
/// </summary>
public enum RuleKind
{
    /// <summary>A pilot flying one of the types, dying.</summary>
    Ship,

    /// <summary>A pilot with one of the types fitted (at least <see cref="ProfileRule.MinFitted"/> of them together), dying.</summary>
    Module,

    /// <summary>One of the types dying, with no pilot (a deployable such as a CRAB beacon).</summary>
    Victim,

    /// <summary>
    /// An attacker whose last hit was one of the types, optionally only on given victims (smart bombs on
    /// capsules: a gate camp, not a ratting smartbomber), and optionally only on its own side or the
    /// other (<see cref="VictimSide"/>).
    /// </summary>
    Weapon,
}

/// <summary>Whose victims a <see cref="RuleKind.Weapon"/> rule counts, by the attacker's corporation and alliance.</summary>
public enum VictimSide
{
    Any,

    /// <summary>The attacker's own corporation or alliance: area weapons hitting fleet mates, a group bombing together.</summary>
    Own,

    /// <summary>
    /// Anyone else: a camp popping strangers' pods, not its own. A kill whose attackers' corporations are
    /// not known counts as the other side.
    /// </summary>
    Other,
}

/// <summary>
/// One way a profile's target shows on a killmail. The types are given by SDE group ids, type ids, or the
/// name of a type whose whole group counts (names survive CCP renumbering; groups catch variants).
/// </summary>
/// <param name="Weight">How much a match counts: 1 for the target itself, less for indirect evidence.</param>
/// <param name="Indicator">Indirect evidence: a sign the target was there, not the target on the killmail.</param>
/// <param name="VictimGroups">For <see cref="RuleKind.Weapon"/>: only kills of these victims (SDE groups) count.</param>
/// <param name="Side">For <see cref="RuleKind.Weapon"/>: whose victims count.</param>
/// <param name="MinFitted">
/// For <see cref="RuleKind.Module"/>: how many of the types, mixed, must be fitted together (five smart
/// bombs make a smartbomber, one is a utility slot).
/// </param>
public sealed record ProfileRule(
    RuleKind Kind,
    int[]? Groups = null,
    int[]? Types = null,
    string? GroupOfType = null,
    double Weight = 1,
    bool Indicator = false,
    int[]? VictimGroups = null,
    VictimSide Side = VictimSide.Any,
    int MinFitted = 1);

/// <param name="Id">Stable id, stored with each sighting and used by the API.</param>
/// <param name="KeepsFights">
/// Bursts of kills are this profile's target (a gate camp), so the battle rule doesn't mark its sightings
/// as fleet fights. Kills with very many attackers still are.
/// </param>
public sealed record HuntingProfile(string Id, string Name, IReadOnlyList<ProfileRule> Rules, bool KeepsFights = false);

/// <summary>
/// The built-in hunting profiles (docs/map-and-activity.md, "Hunting profiles"). Changing one here (or a
/// new SDE build changing what it resolves to) rebuilds its sightings from the stored kills.
/// </summary>
public static class HuntingProfiles
{
    // SDE group ids, checked against the SDE when the profiles are resolved.
    public const int Capsule = 29, Titan = 30, SmartBomb = 72, Dreadnought = 485, Carrier = 547, Supercarrier = 659, Marauder = 900,
        ForceAuxiliary = 1538, VortonProjector = 4060, LancerDreadnought = 4594;

    /// <summary>
    /// A pilot killed by NPCs only counts this many times: it died ratting there, the strongest sign it
    /// comes back. Pilotless victims (CRAB beacons) count as they are.
    /// </summary>
    public const double NpcLossWeight = 2;

    public const int Ishtar = 12005;

    /// <summary>Smart bombs a dying ship needs fitted to count as a smartbomber.</summary>
    public const int SmartbomberMinFitted = 5;

    /// <summary>Its group holds both kinds of CRAB beacon (the CONCORD and the CONCORD-Carrier one).</summary>
    public const string CrabBeacon = "CONCORD Rogue Analysis Beacon";

    /// <summary>
    /// Anchoring a CRAB beacon is mostly a capital ratting habit: a beacon killed in a system means a
    /// capital ratted there, even when no capital is on any killmail.
    /// </summary>
    private static readonly ProfileRule CrabIndicator = new(RuleKind.Victim, GroupOfType: CrabBeacon, Weight: 0.75, Indicator: true);

    public static readonly IReadOnlyList<HuntingProfile> All =
    [
        new("carriers", "Carriers", [new(RuleKind.Ship, Groups: [Carrier]), CrabIndicator]),
        new("supers", "Supercarriers and Titans", [new(RuleKind.Ship, Groups: [Supercarrier, Titan]), CrabIndicator]),
        new("dreads", "Dreadnoughts", [new(RuleKind.Ship, Groups: [Dreadnought, LancerDreadnought])]),
        new("fax", "Force Auxiliaries", [new(RuleKind.Ship, Groups: [ForceAuxiliary])]),
        new("marauders", "Marauders", [new(RuleKind.Ship, Groups: [Marauder])]),
        new("ishtars", "Ishtars", [new(RuleKind.Ship, Types: [Ishtar])]),
        // A victim with at least five smart bombs fitted (any mix): a ship built to bomb, not one carrying a
        // utility smart bomb. Or an attacker's area weapon (a smart bomb, or the Vorton projector of an
        // EDENCOM ship) killing one of its own corporation or alliance: friendly fire in a group that bombs
        // together.
        new("smartbombers", "Smartbombers",
        [
            new(RuleKind.Module, Groups: [SmartBomb], MinFitted: SmartbomberMinFitted),
            new(RuleKind.Weapon, Groups: [SmartBomb, VortonProjector], Side: VictimSide.Own),
        ]),
        new("crab", "CRAB beacons", [new(RuleKind.Victim, GroupOfType: CrabBeacon)]),
        // Strangers' pods popped by smart bombs: PvP smartbombing, typically a camp on a low-sec pipe gate.
        // A group's own pods caught in its bombs are friendly fire, not a camp.
        new("smartbomb-camps", "Smartbomb camps",
            [new(RuleKind.Weapon, Groups: [SmartBomb], VictimGroups: [Capsule], Side: VictimSide.Other)], KeepsFights: true),
    ];
}

/// <summary>A rule with its types resolved against the SDE; <paramref name="Victims"/> null means any victim.</summary>
public sealed record ResolvedRule(
    RuleKind Kind, FrozenSet<int> Types, double Weight, bool Indicator, FrozenSet<int>? Victims = null, VictimSide Side = VictimSide.Any,
    int MinFitted = 1)
{
    /// <summary>
    /// Matching needs to know how many of each type were fitted, not only which: a kill without the counts
    /// (<see cref="Kill.VictimFittedCounts"/>) can only be shown to match by enough distinct types.
    /// </summary>
    public bool NeedsCounts => Kind == RuleKind.Module && MinFitted > 1;
}

/// <summary>A profile ready to match kills, and the definition its sightings are stored under.</summary>
public sealed record ResolvedProfile(HuntingProfile Profile, IReadOnlyList<ResolvedRule> Rules, string Definition)
{
    public string Id => Profile.Id;

    /// <summary>
    /// Resolves each rule's groups and names to type ids. What can't be found is logged and left out
    /// (<paramref name="problems"/>), so a renamed type weakens a profile instead of stopping the service.
    /// </summary>
    public static ResolvedProfile Resolve(HuntingProfile profile, SdeCatalog catalog, ICollection<string> problems)
    {
        var rules = new List<ResolvedRule>();
        foreach (var rule in profile.Rules)
        {
            var groups = new HashSet<int>();
            foreach (var group in rule.Groups ?? [])
            {
                if (catalog.Groups.ContainsKey(group))
                    groups.Add(group);
                else
                    problems.Add($"{profile.Id}: no SDE group {group}");
            }

            if (rule.GroupOfType is { } name)
            {
                if (catalog.FindType(name) is { } named)
                    groups.Add(named.GroupId);
                else
                    problems.Add($"{profile.Id}: no SDE type named \"{name}\"");
            }

            var types = new HashSet<int>(catalog.Types.Values.Where(t => groups.Contains(t.GroupId)).Select(t => t.Id));
            foreach (var type in rule.Types ?? [])
            {
                if (catalog.Types.ContainsKey(type))
                    types.Add(type);
                else
                    problems.Add($"{profile.Id}: no SDE type {type}");
            }

            FrozenSet<int>? victims = null;
            if (rule.VictimGroups is { } victimGroups)
            {
                foreach (var group in victimGroups.Where(g => !catalog.Groups.ContainsKey(g)))
                    problems.Add($"{profile.Id}: no SDE group {group}");
                victims = catalog.Types.Values.Where(t => victimGroups.Contains(t.GroupId)).Select(t => t.Id).ToFrozenSet();
            }

            rules.Add(new ResolvedRule(rule.Kind, types.ToFrozenSet(), rule.Weight, rule.Indicator, victims, rule.Side, rule.MinFitted));
        }

        // What the sightings depend on: each rule's kind, weight, exact types, how many must be fitted,
        // victims and side, whether fights are kept (it changes which sightings are marked as fleet), and
        // how NPC losses count.
        var definition = string.Join(';', rules.Select(r =>
                $"{r.Kind}{(r.Indicator ? "?" : "")}@{r.Weight.ToString(CultureInfo.InvariantCulture)}:{string.Join(',', r.Types.Order())}"
                + (r.MinFitted > 1 ? $"x{r.MinFitted}" : "")
                + (r.Victims is { } v ? $">{string.Join(',', v.Order())}" : "")
                + (r.Side == VictimSide.Any ? "" : $">{r.Side.ToString().ToLowerInvariant()} side")))
            + (profile.KeepsFights ? ";keeps fights" : "")
            + $";npc losses x{HuntingProfiles.NpcLossWeight.ToString(CultureInfo.InvariantCulture)}";
        return new ResolvedProfile(profile, rules, definition);
    }
}

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
/// <param name="AppearanceOnly">
/// For <see cref="RuleKind.Weapon"/>: not evidence on its own, only an appearance
/// (<see cref="HuntingAppearance"/>) towards the regulars of a pilot the profile already knows there.
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
    int MinFitted = 1,
    bool AppearanceOnly = false);

/// <param name="Id">Stable id, stored with each sighting and used by the API.</param>
/// <param name="KeepsFights">
/// Bursts of kills are this profile's target (a gate camp), so the battle rule doesn't mark its sightings
/// as fleet fights. Kills with very many attackers still are.
/// </param>
/// <param name="Description">What the target is, in a sentence, for the clients' info page.</param>
/// <param name="Evidence">
/// What counts as seeing it, one line per way, in players' words: the rules explained. Kept next to the
/// rules so a change to one is a change to the other.
/// </param>
public sealed record HuntingProfile(
    string Id, string Name, IReadOnlyList<ProfileRule> Rules, bool KeepsFights = false, string Description = "", IReadOnlyList<string>? Evidence = null);

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

    /// <summary>
    /// A sighting of someone in their own alliance's sovereignty counts this many times: a local who lives
    /// there, not a random occurrence. It stacks with <see cref="NpcLossWeight"/>.
    /// </summary>
    public const double LocalWeight = 1.5;

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

    private const string CrabEvidence = "Indirect, counting 0.75: a CRAB beacon killed there. Anchoring one is mostly a capital ratting habit.";

    public static readonly IReadOnlyList<HuntingProfile> All =
    [
        new("carriers", "Carriers", [new(RuleKind.Ship, Groups: [Carrier]), CrabIndicator],
            Description: "Carrier pilots, mostly ratting with fighters in their home systems.",
            Evidence: ["A Carrier dies there.", CrabEvidence]),
        new("supers", "Supercarriers and Titans", [new(RuleKind.Ship, Groups: [Supercarrier, Titan]), CrabIndicator],
            Description: "Supercarriers and Titans, which live in their home space and rarely leave it.",
            Evidence: ["A Supercarrier or Titan dies there.", CrabEvidence]),
        new("dreads", "Dreadnoughts", [new(RuleKind.Ship, Groups: [Dreadnought, LancerDreadnought])],
            Description: "Dreadnoughts, ratting or crabbing where they live.",
            Evidence: ["A Dreadnought or Lancer Dreadnought dies there."]),
        new("fax", "Force Auxiliaries", [new(RuleKind.Ship, Groups: [ForceAuxiliary])],
            Description: "Force Auxiliaries: logistics capitals, usually where other capitals live.",
            Evidence: ["A Force Auxiliary dies there."]),
        new("marauders", "Marauders", [new(RuleKind.Ship, Groups: [Marauder])],
            Description: "Marauders: battleships running anomalies, missions and escalations.",
            Evidence: ["A Marauder dies there."]),
        new("ishtars", "Ishtars", [new(RuleKind.Ship, Types: [Ishtar])],
            Description: "Ishtars: the common drone ratting cruiser.",
            Evidence: ["An Ishtar dies there."]),
        // A victim with at least five smart bombs fitted (any mix): a ship built to bomb, not one carrying a
        // utility smart bomb. Or an attacker's area weapon (a smart bomb, or the Vorton projector of an
        // EDENCOM ship) killing one of its own corporation or alliance: friendly fire in a group that bombs
        // together. A smartbomber also shows on the losses of its alts in other corporations: those kills
        // are appearances, towards the regulars of a smartbomber already known there.
        new("smartbombers", "Smartbombers",
        [
            new(RuleKind.Module, Groups: [SmartBomb], MinFitted: SmartbomberMinFitted),
            new(RuleKind.Weapon, Groups: [SmartBomb, VortonProjector], Side: VictimSide.Own),
            new(RuleKind.Weapon, Groups: [SmartBomb, VortonProjector], AppearanceOnly: true),
        ],
            Description: "Smartbomb battleships clearing ratting sites, often next to their own alts.",
            Evidence:
            [
                $"A ship with at least {SmartbomberMinFitted} smart bombs fitted (any mix of sizes and damage types) dies there. One is a utility slot, not a smartbomber.",
                "A smart bomb or Vorton projector kills someone of its own corporation or alliance: friendly fire in a group that bombs together.",
                "A known smartbomber on the attacker list of an alt's loss in another corporation: counts towards its regulars, not the score.",
            ]),
        new("crab", "CRAB beacons", [new(RuleKind.Victim, GroupOfType: CrabBeacon)],
            Description: "CONCORD Rogue Analysis Beacons, anchored to run CRAB sites, mostly by capital ratters.",
            Evidence: ["A CRAB beacon (either kind) is killed there."]),
        // Strangers' pods popped by smart bombs: PvP smartbombing, typically a camp on a low-sec pipe gate.
        // A group's own pods caught in its bombs are friendly fire, not a camp.
        new("smartbomb-camps", "Smartbomb camps",
            [new(RuleKind.Weapon, Groups: [SmartBomb], VictimGroups: [Capsule], Side: VictimSide.Other)], KeepsFights: true,
            Description: "Smartbomb gate camps, typically on a low-sec pipe gate: a danger to pass, and a target to catch.",
            Evidence:
            [
                "A smart bomb's last hit kills the capsule of another corporation and alliance. A group's own pods caught in its bombs are friendly fire.",
                "Bursts of kills are what a camp does, so they are not left out as fleet fights (more than 25 attackers still are).",
            ]),
    ];
}

/// <summary>A rule with its types resolved against the SDE; <paramref name="Victims"/> null means any victim.</summary>
public sealed record ResolvedRule(
    RuleKind Kind, FrozenSet<int> Types, double Weight, bool Indicator, FrozenSet<int>? Victims = null, VictimSide Side = VictimSide.Any,
    int MinFitted = 1, bool AppearanceOnly = false)
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

            rules.Add(new ResolvedRule(rule.Kind, types.ToFrozenSet(), rule.Weight, rule.Indicator, victims, rule.Side, rule.MinFitted, rule.AppearanceOnly));
        }

        // What the sightings depend on: each rule's kind, weight, exact types, how many must be fitted,
        // victims and side, whether fights are kept (it changes which sightings are marked as fleet), and
        // how NPC losses and locals count.
        var definition = string.Join(';', rules.Select(r =>
                $"{r.Kind}{(r.Indicator ? "?" : "")}@{r.Weight.ToString(CultureInfo.InvariantCulture)}:{string.Join(',', r.Types.Order())}"
                + (r.MinFitted > 1 ? $"x{r.MinFitted}" : "")
                + (r.Victims is { } v ? $">{string.Join(',', v.Order())}" : "")
                + (r.Side == VictimSide.Any ? "" : $">{r.Side.ToString().ToLowerInvariant()} side")
                + (r.AppearanceOnly ? ">appearances" : "")))
            + (profile.KeepsFights ? ";keeps fights" : "")
            + $";npc losses x{HuntingProfiles.NpcLossWeight.ToString(CultureInfo.InvariantCulture)}"
            + $";locals x{HuntingProfiles.LocalWeight.ToString(CultureInfo.InvariantCulture)}";
        return new ResolvedProfile(profile, rules, definition);
    }
}

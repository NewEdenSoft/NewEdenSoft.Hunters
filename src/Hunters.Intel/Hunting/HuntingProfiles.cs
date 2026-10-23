using System.Collections.Frozen;
using System.Globalization;
using NewEdenSoft.Hunters.Intel.Sde;

namespace NewEdenSoft.Hunters.Intel.Hunting;

/// <summary>What a rule looks for on a killmail.</summary>
public enum RuleKind
{
    /// <summary>A pilot flying one of the types, as the victim or an attacker.</summary>
    Ship,

    /// <summary>One of the types fitted on the victim, or the weapon an attacker last hit with.</summary>
    Module,

    /// <summary>One of the types dying, with no pilot (a deployable such as a CRAB beacon).</summary>
    Victim,
}

/// <summary>
/// One way a profile's target shows on a killmail. The types are given by SDE group ids, type ids, or the
/// name of a type whose whole group counts (names survive CCP renumbering; groups catch variants).
/// </summary>
/// <param name="Weight">How much a match counts: 1 for the target itself, less for indirect evidence.</param>
/// <param name="Indicator">Indirect evidence: a sign the target was there, not the target on the killmail.</param>
public sealed record ProfileRule(
    RuleKind Kind,
    int[]? Groups = null,
    int[]? Types = null,
    string? GroupOfType = null,
    double Weight = 1,
    bool Indicator = false);

/// <param name="Id">Stable id, stored with each sighting and used by the API.</param>
public sealed record HuntingProfile(string Id, string Name, IReadOnlyList<ProfileRule> Rules);

/// <summary>
/// The built-in hunting profiles (docs/map-and-activity.md, "Hunting profiles"). Changing one here (or a
/// new SDE build changing what it resolves to) rebuilds its sightings from the stored kills.
/// </summary>
public static class HuntingProfiles
{
    // SDE group ids, checked against the SDE when the profiles are resolved.
    public const int Titan = 30, SmartBomb = 72, Dreadnought = 485, Carrier = 547, Supercarrier = 659, Marauder = 900,
        ForceAuxiliary = 1538, LancerDreadnought = 4594;

    public const int Ishtar = 12005;

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
        new("smartbombers", "Smartbombers", [new(RuleKind.Module, Groups: [SmartBomb])]),
        new("crab", "CRAB beacons", [new(RuleKind.Victim, GroupOfType: CrabBeacon)]),
    ];
}

/// <summary>A rule with its types resolved against the SDE.</summary>
public sealed record ResolvedRule(RuleKind Kind, FrozenSet<int> Types, double Weight, bool Indicator);

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

            rules.Add(new ResolvedRule(rule.Kind, types.ToFrozenSet(), rule.Weight, rule.Indicator));
        }

        // What the sightings depend on: each rule's kind, weight and exact types.
        var definition = string.Join(';', rules.Select(r =>
            $"{r.Kind}{(r.Indicator ? "?" : "")}@{r.Weight.ToString(CultureInfo.InvariantCulture)}:{string.Join(',', r.Types.Order())}"));
        return new ResolvedProfile(profile, rules, definition);
    }
}

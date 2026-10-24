using System.Globalization;
using System.Text.Json;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Hunting;
using NewEdenSoft.Hunters.Intel.Kills;
using NewEdenSoft.Hunters.Intel.Sde;
using NewEdenSoft.Hunters.Sde;

namespace NewEdenSoft.Hunters.Intel.Tests.Hunting;

public sealed class SightingMatcherTests
{
    private const long Pilot = 90000001, Other = 90000002, Third = 90000003;
    private const long Corp = 98000001, OtherCorp = 98000002, Alliance = 99000001;
    private static readonly DateTimeOffset Time = new(2026, 10, 23, 5, 46, 27, TimeSpan.Zero);

    internal static async Task<List<ResolvedProfile>> ProfilesAsync(List<string>? problems = null)
    {
        var sde = new FakeSde();
        var catalog = SdeCatalog.Build(await sde.LoadAsync(CancellationToken.None), await ((ISdeTypeSource)sde).LoadAsync(CancellationToken.None));
        problems ??= [];
        return [.. HuntingProfiles.All.Select(p => ResolvedProfile.Resolve(p, catalog, problems))];
    }

    /// <summary>A kill in Jita; the victim is in <see cref="Corp"/>, and every attacker in <paramref name="attackerCorp"/>.</summary>
    internal static Kill Kill(
        int victimShip, long? victim = null, int[]? fitted = null, (long Pilot, int Ship, int Weapon)[]? attackers = null,
        bool npc = false, long attackerCorp = OtherCorp, long victimAlliance = 0, long attackerAlliance = 0) => new()
    {
        KillmailId = 1,
        Time = Time,
        SystemId = FakeSde.Jita,
        Npc = npc,
        VictimCharacterId = victim,
        VictimCorporationId = Corp,
        VictimAllianceId = victimAlliance == 0 ? null : victimAlliance,
        VictimShipTypeId = victimShip,
        VictimFittedTypeIds = fitted ?? [],
        AttackerCount = attackers?.Length ?? 0,
        AttackerCharacterIds = [.. (attackers ?? []).Select(a => a.Pilot)],
        AttackerCorporationIds = [.. (attackers ?? []).Select(a => a.Pilot == 0 ? 0 : attackerCorp)],
        AttackerAllianceIds = [.. (attackers ?? []).Select(a => a.Pilot == 0 ? 0 : attackerAlliance)],
        AttackerShipTypeIds = [.. (attackers ?? []).Select(a => a.Ship)],
        AttackerWeaponTypeIds = [.. (attackers ?? []).Select(a => a.Weapon)],
    };

    private static string Describe(Sighting s) =>
        $"{s.Profile} {s.PilotId} {s.Role} {s.TypeId} {s.Weight.ToString(CultureInfo.InvariantCulture)}{(s.Indicator ? " indicator" : "")}";

    [Fact]
    public async Task A_crab_beacon_killed_by_supers_shows_the_beacon_and_hints_at_capitals_but_not_the_supers_that_killed_it()
    {
        var file = JsonSerializer.Deserialize<KillFeedFile>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Kills", "Samples", "crab-beacon.json")))!;
        var kill = file.ToKill();

        var sightings = SightingMatcher.Match(kill, await ProfilesAsync(), 25);

        // The supers came to kill it and may have left; the beacon says someone ratted there.
        Assert.Equal(
            [
                "carriers 0 Victim 60244 0.75 indicator",
                "crab 0 Victim 60244 1",
                "supers 0 Victim 60244 0.75 indicator",
            ],
            sightings.Select(Describe).Order());
        // The killmail's attackers come with their corporations.
        Assert.Equal(kill.AttackerCount, kill.AttackerCorporationIds.Length);
        Assert.All(kill.AttackerCorporationIds.Where((_, i) => kill.AttackerCharacterIds[i] != 0), c => Assert.NotEqual(0, c));
        Assert.All(sightings, s => Assert.Equal((30000898, new DateTimeOffset(2026, 10, 23, 5, 0, 0, TimeSpan.Zero)), (s.SystemId, s.Hour)));
    }

    [Fact]
    public async Task Both_kinds_of_crab_beacon_count()
    {
        var sightings = SightingMatcher.Match(Kill(FakeSde.CarrierCrabBeacon, victim: Pilot), await ProfilesAsync(), 25);

        Assert.Contains("crab 0 Victim 92183 1", sightings.Select(Describe));
        Assert.Contains("carriers 0 Victim 92183 0.75 indicator", sightings.Select(Describe));
    }

    [Fact]
    public async Task Carriers_are_seen_when_they_die_not_when_they_kill()
    {
        var profiles = await ProfilesAsync();
        var lost = Kill(FakeSde.Thanatos, victim: Pilot, attackers: [(Other, FakeSde.Thanatos, 0), (Third, FakeSde.Rifter, 0)]);
        var killing = Kill(FakeSde.Rifter, victim: Pilot, attackers: [(Other, FakeSde.Thanatos, 0)]);

        Assert.Equal(["carriers 90000001 Victim 23911 1"], SightingMatcher.Match(lost, profiles, 25).Select(Describe));
        Assert.Empty(SightingMatcher.Match(killing, profiles, 25));
    }

    [Fact]
    public async Task A_ship_lost_to_npcs_counts_double()
    {
        var profiles = await ProfilesAsync();

        Assert.Equal(["ishtars 90000001 Victim 12005 2"], SightingMatcher.Match(Kill(FakeSde.Ishtar, victim: Pilot, npc: true), profiles, 25).Select(Describe));
        // A CRAB beacon has no pilot: its evidence counts as it is.
        Assert.Contains("crab 0 Victim 60244 1", SightingMatcher.Match(Kill(FakeSde.CrabBeacon, npc: true), profiles, 25).Select(Describe));
    }

    [Fact]
    public async Task Smartbombers_are_seen_by_the_area_weapon_fitted_or_fired_at_their_own_side()
    {
        var profiles = await ProfilesAsync();
        (long, int, int)[] bombers = [(Other, FakeSde.Rifter, FakeSde.Smartbomb), (Third, FakeSde.Thunderchild, FakeSde.Vorton)];

        // A ship with a smart bomb fitted dying: a smartbomber. Area weapons on another corporation: not.
        Assert.Equal(["smartbombers 90000001 Victim 3993 1"],
            SightingMatcher.Match(Kill(FakeSde.Rifter, victim: Pilot, fitted: [FakeSde.Smartbomb], attackers: bombers), profiles, 25).Select(Describe));

        // Area weapons hitting their own corporation, or their own alliance: a group bombing together.
        Assert.Equal(["smartbombers 90000002 Attacker 3993 1", "smartbombers 90000003 Attacker 54748 1"],
            SightingMatcher.Match(Kill(FakeSde.Rifter, victim: Pilot, attackers: bombers, attackerCorp: Corp), profiles, 25).Select(Describe).Order());
        Assert.Equal(2, SightingMatcher.Match(Kill(FakeSde.Rifter, victim: Pilot, attackers: bombers, victimAlliance: Alliance, attackerAlliance: Alliance), profiles, 25).Count);

        // A kill stored before the attackers' corporations were kept: not known to be their own side.
        var unknown = Kill(FakeSde.Rifter, victim: Pilot, attackers: bombers, attackerCorp: Corp);
        unknown.AttackerCorporationIds = [];
        unknown.AttackerAllianceIds = [];
        Assert.Empty(SightingMatcher.Match(unknown, profiles, 25));
    }

    [Fact]
    public async Task Smartbomb_camps_are_pods_popped_by_smart_bombs()
    {
        var profiles = await ProfilesAsync();
        Kill Pod((long, int, int)[] attackers) => Kill(FakeSde.Capsule, victim: Pilot, attackers: attackers);

        // A pod killed by a smartbombing attacker: a camp.
        Assert.Equal(
            ["smartbomb-camps 90000002 Attacker 3993 1"],
            SightingMatcher.Match(Pod([(Other, FakeSde.Rifter, FakeSde.Smartbomb), (Third, FakeSde.Rifter, 2873)]), profiles, 25).Select(Describe).Order());

        // A ship (not a pod) smartbombed: not a camp.
        var frigate = Kill(FakeSde.Rifter, victim: Pilot, attackers: [(Other, FakeSde.Rifter, FakeSde.Smartbomb)]);
        Assert.DoesNotContain(SightingMatcher.Match(frigate, profiles, 25), s => s.Profile == "smartbomb-camps");

        // A group's own pod caught in its bombs: friendly fire (a smartbomber), not a camp.
        Assert.Equal(["smartbombers 90000002 Attacker 3993 1"],
            SightingMatcher.Match(Kill(FakeSde.Capsule, victim: Pilot, attackers: [(Other, FakeSde.Rifter, FakeSde.Smartbomb)], attackerCorp: Corp), profiles, 25)
                .Select(Describe));
    }

    [Fact]
    public async Task Ishtars_are_one_ship_type()
    {
        var sightings = SightingMatcher.Match(Kill(FakeSde.Ishtar, victim: Pilot), await ProfilesAsync(), 25);

        Assert.Equal("ishtars 90000001 Victim 12005 1", Describe(Assert.Single(sightings)));
    }

    [Fact]
    public async Task More_attackers_than_the_limit_is_a_fleet_fight()
    {
        var profiles = await ProfilesAsync();
        (long, int, int)[] Attackers(int count) => [.. Enumerable.Range(0, count).Select(i => ((long)(Other + i), FakeSde.Rifter, 0))];

        Assert.False(Assert.Single(SightingMatcher.Match(Kill(FakeSde.Thanatos, Pilot, attackers: Attackers(25)), profiles, 25)).IsFleet);
        Assert.True(Assert.Single(SightingMatcher.Match(Kill(FakeSde.Thanatos, Pilot, attackers: Attackers(26)), profiles, 25)).IsFleet);
    }

    [Fact]
    public async Task Profiles_resolve_groups_and_names_to_types_and_report_what_is_missing()
    {
        var problems = new List<string>();
        var profiles = await ProfilesAsync(problems);

        var crab = profiles.Single(p => p.Id == "crab");
        Assert.Equal([FakeSde.CrabBeacon, FakeSde.CarrierCrabBeacon], crab.Rules[0].Types.Order()); // Not the unpublished copy's group.
        Assert.Contains("Victim@1:60244,92183", crab.Definition);
        Assert.Equal([FakeSde.Hel, FakeSde.Wyvern], profiles.Single(p => p.Id == "supers").Rules[0].Types.Order());

        // The fake SDE has no Titans, Dreadnoughts, Force Auxiliaries or Marauders.
        Assert.Contains("supers: no SDE group 30", problems);
        Assert.Contains("dreads: no SDE group 485", problems);
        Assert.Empty(profiles.Single(p => p.Id == "marauders").Rules[0].Types);

        // The camp rule: smart bombs, on capsules only, keeping fights.
        var camps = profiles.Single(p => p.Id == "smartbomb-camps");
        Assert.Equal([FakeSde.Capsule], camps.Rules[0].Victims!);
        Assert.Equal("Weapon@1:3993>670>other side;keeps fights;npc losses x2", camps.Definition);
        Assert.Equal("Module@1:3993,54748;Weapon@1:3993,54748>own side;npc losses x2", profiles.Single(p => p.Id == "smartbombers").Definition);
    }
}

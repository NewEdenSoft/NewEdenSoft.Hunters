using NewEdenSoft.Hunters.Contracts.Intel;
using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class NearbyTests
{
    private static readonly NearbySetting Defaults = new();

    [Fact]
    public void Keeps_a_valid_setting()
    {
        Assert.Equal(new NearbySetting(10, true, "carriers", true), Nearby.Validate(new NearbySetting(10, true, "carriers", true)));
        Assert.Equal(new NearbySetting(null, true, "activity", false), Nearby.Validate(new NearbySetting(null, true, "activity")));
    }

    [Fact]
    public void Falls_back_to_the_defaults_for_anything_else()
    {
        Assert.Equal(Defaults, Nearby.Validate(null));
        Assert.Equal(Defaults, Nearby.Validate(new NearbySetting(7, false, "")));
    }

    [Fact]
    public void Asks_from_the_focused_system_for_everything_within_the_light_years()
    {
        Assert.Equal(
            new NearbyRequest(30000142, 8, "carriers", "npc-rising", Nearby.Fetch),
            Nearby.Request(Defaults with { Jumps = 5, ByJumpRange = true, Sort = "carriers" }, 30000142, 8, ["carriers"]));
    }

    [Fact]
    public void Never_asks_by_gate_jumps_they_are_cut_here()
    {
        Assert.Equal(8, Nearby.Request(Defaults with { Jumps = 5, ByJumpRange = false }, 1, 8, [])!.Ly);
        Assert.Equal(Nearby.Request(Defaults with { Jumps = 3 }, 1, 8, []), Nearby.Request(Defaults with { Jumps = 15 }, 1, 8, []));
    }

    [Fact]
    public void Within_jumps_keeps_the_rows_up_to_the_gate_jumps_chosen()
    {
        Assert.True(Nearby.WithinJumps(5, 5));
        Assert.False(Nearby.WithinJumps(6, 5));
        Assert.False(Nearby.WithinJumps(null, 5));
        Assert.True(Nearby.WithinJumps(null, null));
        Assert.True(Nearby.WithinJumps(40, null));
    }

    [Fact]
    public void Sorts_by_activity_for_a_profile_the_server_does_not_list_and_by_the_most_NPC_kills_when_asked()
    {
        var gone = Nearby.Request(Defaults with { Sort = "gone" }, 1, 8, ["carriers"]);
        Assert.Null(gone!.Profile);
        Assert.Equal("npc-rising", gone.Sort);

        var kills = Nearby.Request(Defaults with { Sort = "npc-kills" }, 1, 8, ["carriers"]);
        Assert.Null(kills!.Profile);
        Assert.Equal("npc-kills", kills.Sort);
    }

    [Fact]
    public void Asks_nothing_without_a_focus_or_with_neither_gates_nor_jump_range()
    {
        Assert.Null(Nearby.Request(Defaults, null, 8, []));
        Assert.Null(Nearby.Request(Defaults with { Jumps = null, ByJumpRange = false }, 1, 8, []));
    }

    private static ReachMetric Metric(int lastHour, int day) => new(lastHour, 0, day, null, null, null);

    private static ReachActivity Npc(int lastHour) => Quiet with { NpcKills = Metric(lastHour, lastHour) };

    private static ReachProfile Seen(double likelyNow) => new("ratters", 5, DateTimeOffset.Parse("2026-10-20T00:00:00Z"), likelyNow, null);

    [Fact]
    public void Target_chance_is_nothing_without_kills_or_profiles_likely_now()
    {
        Assert.Equal(0, Nearby.TargetChance(Npc(0), [Seen(0)]));
        Assert.Equal(0, Nearby.TargetChance(null, []));
    }

    [Fact]
    public void Target_chance_grows_with_NPC_kills_and_the_sightings_expected_this_hour_short_of_certain()
    {
        Assert.Equal(1 - Math.Exp(-1), Nearby.TargetChance(Npc(40), []), 9);
        Assert.Equal(1 - Math.Exp(-1), Nearby.TargetChance(Npc(0), [Seen(0.5), Seen(0.5)]), 9);
        var both = Nearby.TargetChance(Npc(40), [Seen(1)]);
        Assert.Equal(1 - Math.Exp(-2), both, 9);
        Assert.True(both < 1);
    }

    [Fact]
    public void Sorts_the_best_chance_first_then_the_nearest()
    {
        (int Id, double Chance, int? Jumps, double Ly)[] rows = [(1, 0.1, 1, 1), (2, 0.9, 4, 1), (3, 0.9, 2, 1), (4, 0.9, null, 1), (5, 0.9, 2, 0.5)];
        Assert.Equal([5, 3, 2, 4, 1], Nearby.ByTargetChance(rows, r => (r.Chance, r.Jumps, r.Ly)).Select(r => r.Id));
    }

    [Fact]
    public void Asks_the_server_for_the_busiest_systems_when_sorting_by_target_chance()
    {
        Assert.Equal("npc-kills", Nearby.NpcSort(Nearby.SortTarget));
        Assert.Equal("npc-rising", Nearby.NpcSort(Nearby.SortActivity));
    }

    private static readonly ReachActivity Quiet = new(Metric(0, 0), Metric(0, 0), Metric(0, 0), Metric(400, 9000));

    [Fact]
    public void Is_active_with_any_kill_in_the_last_hour_or_day_or_a_profile_seen()
    {
        Assert.True(Nearby.IsActive(Quiet with { NpcKills = Metric(3, 3) }, 0));
        Assert.True(Nearby.IsActive(Quiet with { ShipKills = Metric(0, 1) }, 0));
        Assert.True(Nearby.IsActive(Quiet with { PodKills = Metric(0, 2) }, 0));
        Assert.True(Nearby.IsActive(Quiet, 1));
    }

    [Fact]
    public void Leaves_out_systems_with_only_traffic_and_keeps_the_ones_that_cannot_be_judged()
    {
        Assert.False(Nearby.IsActive(Quiet, 0));
        Assert.True(Nearby.IsActive(null, 0));
    }

    [Fact]
    public void Distance_shows_what_was_asked_for()
    {
        Assert.Equal("3 j", Nearby.DistanceText(3, 4.24, true, false));
        Assert.Equal("4.2 ly", Nearby.DistanceText(3, 4.24, false, true));
        Assert.Equal("12 j · 13 ly", Nearby.DistanceText(12, 12.6, true, true));
        Assert.Equal("no gates · 7.1 ly", Nearby.DistanceText(null, 7.1, true, true));
    }

    [Fact]
    public void In_jump_range_is_a_cyno_target_within_the_light_years()
    {
        Assert.True(Nearby.InJumpRange(true, 7.9, 8));
        Assert.False(Nearby.InJumpRange(false, 2, 8));
        Assert.False(Nearby.InJumpRange(true, 8.1, 8));
        Assert.False(Nearby.InJumpRange(true, 1, null));
    }
}

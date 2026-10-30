using NewEdenSoft.Hunters.Client;
using NewEdenSoft.Hunters.Contracts.Kills;
using NewEdenSoft.Hunters.Desktop.Map;
using NewEdenSoft.Hunters.Desktop.Widgets.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class RecentKillsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 8, 0, 0, TimeSpan.Zero);

    private static readonly RecentKill Kill = new(
        1,
        "h",
        Now.AddHours(-1),
        Npc: false,
        Solo: false,
        TotalValue: 3_200_000,
        Victim: new KillParty(90, 98, null, 587),
        FinalBlow: new KillParty(null, null, null, 30428),
        AttackerCount: 3);

    private static readonly Dictionary<long, string> Names = new()
    {
        [90] = "Victim",
        [98] = "Corp",
        [587] = "Rifter",
        [30428] = "Guristas Thug",
        [91] = "Hunter",
        [11] = "Sabre",
    };

    [Fact]
    public void Collects_every_id_once() =>
        Assert.Equal([90, 98, 587, 30428], SystemInfo.KillNameIds([Kill, Kill with { FinalBlow = null }]).Order());

    [Fact]
    public void Names_the_pilot_else_the_corporation_else_the_ship()
    {
        Assert.Equal("Victim", SystemInfo.PartyName(Kill.Victim, Names.GetValueOrDefault));
        Assert.Equal("Corp", SystemInfo.PartyName(Kill.Victim with { CharacterId = null }, Names.GetValueOrDefault));
        Assert.Equal("Guristas Thug", SystemInfo.PartyName(Kill.FinalBlow!, Names.GetValueOrDefault));
    }

    [Fact]
    public void A_row_tells_the_ship_the_pilot_by_whom_when_and_the_value()
    {
        var row = new KillRowViewModel(Kill, Names.GetValueOrDefault, Now);

        Assert.Equal("Rifter", row.Ship);
        Assert.Equal("Victim", row.Victim);
        Assert.Equal("by Guristas Thug", row.By);
        // An NPC's name is its ship already: only the other attackers.
        Assert.Equal(" +2", row.ByExtra);
        Assert.Equal("1h", row.Ago);
        Assert.Equal("3.2M", row.Value);
        Assert.Equal("https://zkillboard.com/kill/1/", row.Url.AbsoluteUri);
    }

    [Fact]
    public void A_pilots_final_blow_names_its_ship()
    {
        var row = new KillRowViewModel(Kill with { FinalBlow = new KillParty(91, null, null, 11), AttackerCount = 1 }, Names.GetValueOrDefault, Now);

        Assert.Equal("by Hunter", row.By);
        Assert.Equal(" · Sabre", row.ByExtra);
    }

    [Fact]
    public void Without_the_final_blow_it_counts_the_attackers_and_unknown_names_wait()
    {
        var row = new KillRowViewModel(Kill with { FinalBlow = null, Npc = true }, _ => null, Now);

        Assert.Equal("…", row.Ship);
        Assert.Equal("", row.By);
        Assert.Equal("3 attackers", row.ByExtra);
        Assert.Equal("NPC ", row.NpcText);
    }

    [Fact]
    public void Ship_icons_come_from_the_image_server() =>
        Assert.Equal("https://images.evetech.net/types/587/icon?size=32", EveImages.TypeIcon(587, 32).AbsoluteUri);
}

using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NewEdenSoft.Hunters.Intel.Kills;

namespace NewEdenSoft.Hunters.Intel.Tests.Kills;

/// <summary>The R2Z2 client and the slim kill, against real files saved from the feed.</summary>
public sealed class KillFeedSourceTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Kills", "Samples", name + ".json"));

    private sealed class FakeR2Z2(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>The source as the intel service registers it, over a fake R2Z2 (and optionally a fake clock for its gate).</summary>
    private static (IKillFeedSource Source, FakeR2Z2 Server) Source(Func<HttpRequestMessage, HttpResponseMessage> answer, TimeProvider? time = null)
    {
        var server = new FakeR2Z2(answer);
        var services = new ServiceCollection();
        services.AddOptions<IntelOptions>().Configure(o => o.Esi.Contact = "https://hunters.example");
        services.AddIntelServices();
        if (time is not null)
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(time);
        }

        services.AddHttpClient<IKillFeedSource, R2Z2KillFeedSource>(R2Z2KillFeedSource.Configure)
            .ConfigurePrimaryHttpMessageHandler(() => server);
        return (services.BuildServiceProvider().GetRequiredService<IKillFeedSource>(), server);
    }

    [Fact]
    public async Task Reads_the_announced_sequence_and_files_with_a_contact_in_the_user_agent()
    {
        var (source, server) = Source(r => r.RequestUri!.AbsolutePath.EndsWith("sequence.json")
            ? Json("""{"sequence":100170177}""")
            : Json(Sample("crab-beacon")));

        Assert.Equal(100170177, await source.GetLatestSequenceAsync(CancellationToken.None));
        var file = await source.GetAsync(100170137, CancellationToken.None);

        Assert.Equal(139358232, file!.KillmailId);
        Assert.Equal(
            ["https://r2z2.zkillboard.com/ephemeral/sequence.json", "https://r2z2.zkillboard.com/ephemeral/100170137.json"],
            server.Requests.Select(r => r.RequestUri!.ToString()));
        Assert.Contains("hunters.example", server.Requests[0].Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task A_missing_file_is_null_and_other_errors_throw()
    {
        var (source, _) = Source(r => new HttpResponseMessage(r.RequestUri!.AbsolutePath.EndsWith("1.json") ? HttpStatusCode.NotFound : HttpStatusCode.BadGateway));

        Assert.Null(await source.GetAsync(1, CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() => source.GetAsync(2, CancellationToken.None));
    }

    [Fact]
    public async Task A_block_says_how_long_to_pause_or_an_hour()
    {
        var (source, _) = Source(r =>
        {
            if (r.RequestUri!.AbsolutePath.EndsWith("sequence.json"))
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));
            return limited;
        });

        Assert.Equal(TimeSpan.FromMinutes(2), (await Assert.ThrowsAsync<KillFeedBlockedException>(() => source.GetAsync(1, CancellationToken.None))).RetryAfter);
        Assert.Equal(TimeSpan.FromHours(1), (await Assert.ThrowsAsync<KillFeedBlockedException>(() => source.GetLatestSequenceAsync(CancellationToken.None))).RetryAfter);
    }

    [Fact]
    public async Task Every_request_waits_its_turn_at_the_gate()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var (source, server) = Source(r => r.RequestUri!.AbsolutePath.EndsWith("sequence.json")
            ? Json("""{"sequence":5}""")
            : new HttpResponseMessage(HttpStatusCode.NotFound), time);

        // 5 a second: one request every 200 ms, files and the announced sequence alike.
        await source.GetAsync(1, CancellationToken.None);
        var second = source.GetLatestSequenceAsync(CancellationToken.None);
        Assert.False(second.IsCompleted);
        Assert.Single(server.Requests);

        time.Advance(TimeSpan.FromMilliseconds(199));
        Assert.False(second.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(5, await second);
        Assert.Equal(2, server.Requests.Count);
    }

    [Fact]
    public async Task A_crab_beacon_killed_by_capitals_keeps_every_attackers_pilot_ship_and_weapon()
    {
        var (source, _) = Source(_ => Json(Sample("crab-beacon")));
        var kill = (await source.GetAsync(100170137, CancellationToken.None))!.ToKill();

        Assert.Equal((139358232L, "1df1b282e834afdb1946a9466678efcf874dc3be", 100170137L), (kill.KillmailId, kill.Hash, kill.Sequence));
        Assert.Equal((new DateTimeOffset(2026, 10, 23, 5, 46, 27, TimeSpan.Zero), 30000898), (kill.Time, kill.SystemId));
        Assert.Equal((false, false, 239824780.47), (kill.Npc, kill.Solo, kill.TotalValue));
        Assert.Equal(((long?)2115996421, (long?)98540583, (long?)99003581, 60244), (kill.VictimCharacterId, kill.VictimCorporationId, kill.VictimAllianceId, kill.VictimShipTypeId));
        Assert.Empty(kill.VictimFittedTypeIds); // Only cargo.

        // A Wyvern (supercarrier) and a Hel.
        Assert.Equal(2, kill.AttackerCount);
        Assert.Equal([2114733361L, 2114613098L], kill.AttackerCharacterIds);
        Assert.Equal([23917, 22852], kill.AttackerShipTypeIds);
        Assert.Equal([40562, 40566], kill.AttackerWeaponTypeIds);
        Assert.Equal(1, kill.FinalBlow); // The Hel.
    }

    [Fact]
    public async Task Npc_attackers_have_no_pilot_and_only_fitted_modules_are_kept()
    {
        var (source, _) = Source(_ => Json(Sample("npc-attackers")));
        var kill = (await source.GetAsync(100170172, CancellationToken.None))!.ToKill();

        Assert.True(kill.Npc);
        Assert.Null(kill.VictimAllianceId);
        Assert.Equal([4025, 4573, 4871, 5849, 6811, 12052, 16391, 23089, 30999, 31047], kill.VictimFittedTypeIds);
        Assert.Equal([0L, 3019609L, 0L], kill.AttackerCharacterIds);
        Assert.Equal([48092, 47953, 30428], kill.AttackerShipTypeIds);
        Assert.Equal([0, 34580, 0], kill.AttackerWeaponTypeIds);
        Assert.Equal(2, kill.FinalBlow); // An NPC: no pilot.
    }

    [Fact]
    public async Task A_victim_without_a_pilot_has_no_character()
    {
        var (source, _) = Source(_ => Json(Sample("no-victim-pilot")));
        var kill = (await source.GetAsync(100170142, CancellationToken.None))!.ToKill();

        Assert.Null(kill.VictimCharacterId);
        Assert.Equal(46363, kill.VictimShipTypeId);
        Assert.Equal(9, kill.AttackerCharacterIds.Length);
        Assert.All(kill.AttackerShipTypeIds, s => Assert.Equal(29988, s));
        Assert.Equal(6, kill.FinalBlow);
    }
}

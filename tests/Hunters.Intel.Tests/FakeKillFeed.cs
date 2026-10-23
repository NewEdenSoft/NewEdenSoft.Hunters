using NewEdenSoft.Hunters.Intel.Kills;

namespace NewEdenSoft.Hunters.Intel.Tests;

/// <summary>R2Z2 in memory: files by sequence and the announced newest sequence; throws <see cref="Failure"/> when set.</summary>
public sealed class FakeKillFeed : IKillFeedSource
{
    public Dictionary<long, KillFeedFile> Files { get; } = [];

    public long Latest { get; set; }

    public Exception? Failure { get; set; }

    public List<long> Requested { get; } = [];

    public Task<long> GetLatestSequenceAsync(CancellationToken cancellationToken) =>
        Failure is { } e ? Task.FromException<long>(e) : Task.FromResult(Latest);

    public Task<KillFeedFile?> GetAsync(long sequence, CancellationToken cancellationToken)
    {
        if (Failure is { } e)
            return Task.FromException<KillFeedFile?>(e);
        Requested.Add(sequence);
        return Task.FromResult(Files.GetValueOrDefault(sequence));
    }

    /// <summary>Publishes a kill at <paramref name="sequence"/> and announces it.</summary>
    public KillFeedFile Publish(long sequence, long killmailId, DateTimeOffset time, int systemId = FakeSde.Jita, bool announce = true, int victimShip = 670)
    {
        var file = new KillFeedFile(killmailId, $"hash{killmailId}", sequence,
            new FeedKillmail(time, systemId, new FeedVictim(90000001, 98000001, null, victimShip, []), [new FeedAttacker(90000002, 587, 2873)]),
            new FeedZkb(10_000, false, true));
        Files[sequence] = file;
        if (announce)
            Latest = Math.Max(Latest, sequence);
        return file;
    }
}

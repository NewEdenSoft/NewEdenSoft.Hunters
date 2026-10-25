using NewEdenSoft.Hunters.Intel.Kills;

namespace NewEdenSoft.Hunters.Intel.Tests;

/// <summary>ESI's killmails, set by the test (a missing one is gone from ESI); records each call.</summary>
public sealed class FakeKillmails : IKillmailSource
{
    public Dictionary<long, KillmailFittings> Known { get; } = [];

    public List<long> Calls { get; } = [];

    /// <summary>Thrown by the next call instead of answering, once.</summary>
    public Exception? Next { get; set; }

    public Task<KillmailFittings?> GetFittingsAsync(long killmailId, string hash, CancellationToken cancellationToken)
    {
        Calls.Add(killmailId);
        if (Next is { } e)
        {
            Next = null;
            return Task.FromException<KillmailFittings?>(e);
        }

        return Task.FromResult(Known.GetValueOrDefault(killmailId));
    }
}

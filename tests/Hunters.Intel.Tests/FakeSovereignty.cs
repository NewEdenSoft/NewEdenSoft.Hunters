using NewEdenSoft.Hunters.Intel.Sovereignty;

namespace NewEdenSoft.Hunters.Intel.Tests;

/// <summary>ESI's sovereignty list, set by the test; <see cref="Failure"/> makes the next call fail.</summary>
public sealed class FakeSovereignty : ISovereigntySource
{
    public List<SovereignClaim> Claims { get; } = [];

    public DateTimeOffset? Expires { get; set; }

    public Exception? Failure { get; set; }

    public Task<SovereigntyList> GetAsync(CancellationToken cancellationToken) =>
        Failure is { } e ? Task.FromException<SovereigntyList>(e) : Task.FromResult(new SovereigntyList([.. Claims], Expires));
}

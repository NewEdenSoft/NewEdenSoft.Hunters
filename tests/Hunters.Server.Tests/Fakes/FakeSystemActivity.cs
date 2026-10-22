using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Tests.Fakes;

/// <summary>ESI's hourly kills and jumps, set by the test; throws <see cref="Failure"/> when set.</summary>
public sealed class FakeSystemActivity : ISystemActivitySource
{
    public HourlyActivity<SystemKills> Kills { get; set; } = new([], DateTimeOffset.UnixEpoch, null);

    public HourlyActivity<SystemJumps> Jumps { get; set; } = new([], DateTimeOffset.UnixEpoch, null);

    public Exception? Failure { get; set; }

    public Task<HourlyActivity<SystemKills>> GetKillsAsync(CancellationToken cancellationToken) =>
        Failure is { } e ? Task.FromException<HourlyActivity<SystemKills>>(e) : Task.FromResult(Kills);

    public Task<HourlyActivity<SystemJumps>> GetJumpsAsync(CancellationToken cancellationToken) =>
        Failure is { } e ? Task.FromException<HourlyActivity<SystemJumps>>(e) : Task.FromResult(Jumps);

    /// <summary>One hour of activity ending at <paramref name="periodEnd"/>, expiring an hour later.</summary>
    public void SetHour(DateTimeOffset periodEnd, SystemKills[] kills, SystemJumps[] jumps)
    {
        Kills = new HourlyActivity<SystemKills>(kills, periodEnd, periodEnd.AddHours(1));
        Jumps = new HourlyActivity<SystemJumps>(jumps, periodEnd, periodEnd.AddHours(1));
    }
}

namespace NewEdenSoft.Hunters.Server.Infrastructure.Presence;

/// <summary>Periodically purges expired entries from <see cref="InMemoryPresenceStore"/>.</summary>
public sealed class PresenceSweeper(InMemoryPresenceStore store, TimeProvider timeProvider) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            store.PurgeExpired();
    }
}

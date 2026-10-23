using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Intel.Hunting;
using NewEdenSoft.Hunters.Intel.Sde;
using NewEdenSoft.Hunters.Intel.Kills;

namespace NewEdenSoft.Hunters.Intel.Tests.Kills;

[Collection(IntelDatabaseCollection.Name)]
public sealed class KillFeedServiceTests(IntelDatabase database) : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 6, 0, 0, TimeSpan.Zero);

    private readonly FakeKillFeed _feed = new();
    private readonly FakeTimeProvider _time = new(Now);
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await RestartAsync();
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        await db.Kills.ExecuteDeleteAsync();
        await db.FeedPositions.ExecuteDeleteAsync();
        await db.Sightings.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    /// <summary>A fresh service, as after a restart (the database stays), with the SDE and hunting profiles loaded.</summary>
    private async Task RestartAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
        _services = database.Services(new FakeSystemActivity(), time: _time, kills: _feed);
        await _services.GetRequiredService<SdeCatalogService>().LoadAsync(CancellationToken.None);
        await _services.GetRequiredService<HuntingProfileService>().SyncAsync(
            _services.GetRequiredService<SdeCatalogStore>().Current!, CancellationToken.None);
    }

    private KillFeedService Reader => _services.GetRequiredService<KillFeedService>();

    private async Task<TimeSpan[]> StepsAsync(int count)
    {
        var delays = new TimeSpan[count];
        for (var i = 0; i < count; i++)
            delays[i] = await Reader.StepAsync(CancellationToken.None);
        return delays;
    }

    private async Task<(List<Kill> Kills, FeedPosition? Position)> StoredAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        return (await db.Kills.OrderBy(k => k.Sequence).ToListAsync(), await db.FeedPositions.SingleOrDefaultAsync());
    }

    [Fact]
    public async Task Starts_at_the_newest_file_and_follows_the_feed()
    {
        _feed.Publish(99, 1099, Now.AddMinutes(-5)); // Before we started: not read.
        _feed.Publish(100, 1100, Now.AddMinutes(-1));

        var delays = new List<TimeSpan>(await StepsAsync(1));
        _feed.Publish(101, 1101, Now);
        delays.AddRange(await StepsAsync(2));

        var (kills, position) = await StoredAsync();
        Assert.Equal([1100L, 1101L], kills.Select(k => k.KillmailId));
        Assert.Equal((101L, Now), (position!.Sequence, position.UpdatedAt));
        Assert.Equal(
            [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(6)],
            delays);
        Assert.Equal(102, Reader.Next);
    }

    [Fact]
    public async Task Resumes_after_its_saved_position_after_a_restart()
    {
        _feed.Publish(100, 1100, Now);
        await StepsAsync(1);

        _feed.Publish(101, 1101, Now);
        _feed.Publish(102, 1102, Now);
        await RestartAsync();
        await StepsAsync(2);

        Assert.Equal([1100L, 1101L, 1102L], (await StoredAsync()).Kills.Select(k => k.KillmailId));
    }

    [Fact]
    public async Task A_position_older_than_the_feed_keeps_files_skips_to_the_newest()
    {
        _feed.Publish(100, 1100, Now);
        await StepsAsync(1);

        _time.Advance(TimeSpan.FromDays(2));
        _feed.Publish(101, 1101, Now);
        _feed.Publish(5000, 6000, Now);
        await RestartAsync();
        await StepsAsync(1);

        Assert.Equal([1100L, 6000L], (await StoredAsync()).Kills.Select(k => k.KillmailId));
    }

    [Fact]
    public async Task Waits_for_files_not_written_yet_and_skips_files_that_are_gone()
    {
        _feed.Publish(100, 1100, Now);
        await StepsAsync(1);

        // 101 is announced but missing (zKillboard skipped it): skip it straight away.
        _feed.Latest = 101;
        Assert.Equal(TimeSpan.FromMilliseconds(100), await Reader.StepAsync(CancellationToken.None));
        Assert.Equal(102, Reader.Next);

        // 102 isn't announced yet: wait the 6 seconds zKillboard asks for, and ask again.
        Assert.Equal(TimeSpan.FromSeconds(6), await Reader.StepAsync(CancellationToken.None));
        Assert.Equal(102, Reader.Next);
        _feed.Publish(102, 1102, Now);
        await StepsAsync(1);

        Assert.Equal([1100L, 1102L], (await StoredAsync()).Kills.Select(k => k.KillmailId));
    }

    [Fact]
    public async Task A_kill_announced_twice_is_stored_once()
    {
        _feed.Publish(100, 1100, Now);
        await StepsAsync(1);
        _feed.Publish(101, 1100, Now);
        await StepsAsync(1);

        var (kills, position) = await StoredAsync();
        Assert.Equal(100, Assert.Single(kills).Sequence);
        Assert.Equal(101, position!.Sequence);
    }

    [Fact]
    public async Task Kills_and_their_sightings_past_the_retention_are_deleted()
    {
        _feed.Publish(100, 1100, Now.AddDays(-91), victimShip: FakeSde.Thanatos);
        await StepsAsync(1);
        _feed.Publish(101, 1101, Now.AddDays(-89), victimShip: FakeSde.Thanatos);
        await StepsAsync(1);
        var recorded = await SightingKillsAsync();
        Assert.Equal([1100L, 1101L], recorded);

        // Pruning runs at most hourly.
        _time.Advance(TimeSpan.FromMinutes(61));
        await StepsAsync(1);

        Assert.Equal([1101L], (await StoredAsync()).Kills.Select(k => k.KillmailId));
        var kept = await SightingKillsAsync();
        Assert.Equal([1101L], kept);
    }

    [Fact]
    public async Task Each_kill_records_its_sightings()
    {
        _feed.Publish(100, 1100, Now, victimShip: FakeSde.Thanatos);
        await StepsAsync(1);
        _feed.Publish(101, 1101, Now.AddHours(1), victimShip: FakeSde.Rifter);
        await StepsAsync(1);

        await using var scope = _services.CreateAsyncScope();
        var sighting = Assert.Single(await scope.ServiceProvider.GetRequiredService<IntelDbContext>().Sightings.ToListAsync());
        Assert.Equal(("carriers", 90000001L, SightingRole.Victim, FakeSde.Thanatos), (sighting.Profile, sighting.PilotId, sighting.Role, sighting.TypeId));
    }

    private async Task<long[]> SightingKillsAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        return [.. await db.Sightings.Select(s => s.KillmailId).OrderBy(k => k).ToListAsync()];
    }

    [Fact]
    public async Task Failures_back_off_and_keep_the_position()
    {
        _feed.Publish(100, 1100, Now);
        await StepsAsync(1);

        _feed.Failure = new HttpRequestException("zKillboard is down");
        Assert.Equal([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2)], await StepsAsync(2));

        _feed.Failure = null;
        _feed.Publish(101, 1101, Now);
        Assert.Equal(TimeSpan.FromMilliseconds(100), await Reader.StepAsync(CancellationToken.None));
        Assert.Equal(2, (await StoredAsync()).Kills.Count);
    }
}

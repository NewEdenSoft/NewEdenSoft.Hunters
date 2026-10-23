using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Intel.Activity;
using NewEdenSoft.Hunters.Intel.Data;

namespace NewEdenSoft.Hunters.Intel.Tests.Activity;

[Collection(IntelDatabaseCollection.Name)]
public sealed class SystemActivityTests(IntelDatabase database) : IAsyncLifetime
{
    private const int Jita = 30000142, Perimeter = 30000144;

    /// <summary>The start of the current hour plus 48 seconds, like ESI's Last-Modified.</summary>
    private static readonly DateTimeOffset ThisHour =
        new DateTimeOffset(DateTime.UtcNow.Date.AddHours(DateTime.UtcNow.Hour), TimeSpan.Zero).AddSeconds(48);

    private readonly FakeSystemActivity _esi = new();
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _services = database.Services(_esi);
        // Tests share one database: start without other tests' hours.
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        await db.SystemKills.ExecuteDeleteAsync();
        await db.SystemJumps.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    private SystemActivityService Worker => _services.GetRequiredService<SystemActivityService>();

    private async Task<(List<SystemKillsSnapshot> Kills, List<SystemJumpsSnapshot> Jumps)> StoredAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        return (await db.SystemKills.OrderBy(s => s.PeriodEnd).ToListAsync(), await db.SystemJumps.OrderBy(s => s.PeriodEnd).ToListAsync());
    }

    [Fact]
    public async Task Stores_an_hour_of_kills_and_jumps_as_one_row_each()
    {
        _esi.SetHour(ThisHour,
            [new SystemKills(Jita, 3, 1, 40), new SystemKills(Perimeter, 0, 0, 12)],
            [new SystemJumps(Jita, 1500), new SystemJumps(Perimeter, 900)]);

        await Worker.PollAsync(CancellationToken.None);

        var (kills, jumps) = await StoredAsync();
        var hour = Assert.Single(kills);
        Assert.Equal(ThisHour, hour.PeriodEnd);
        Assert.Equal([Jita, Perimeter], hour.SystemIds);
        Assert.Equal([3, 0], hour.ShipKills);
        Assert.Equal([1, 0], hour.PodKills);
        Assert.Equal([40, 12], hour.NpcKills);
        Assert.Equal([1500, 900], Assert.Single(jumps).ShipJumps);
    }

    [Fact]
    public async Task Stores_each_hour_once()
    {
        _esi.SetHour(ThisHour.AddHours(-1), [new SystemKills(Jita, 1, 0, 0)], [new SystemJumps(Jita, 10)]);
        await Worker.PollAsync(CancellationToken.None);
        await Worker.PollAsync(CancellationToken.None);

        _esi.SetHour(ThisHour, [new SystemKills(Jita, 2, 0, 0)], [new SystemJumps(Jita, 20)]);
        await Worker.PollAsync(CancellationToken.None);

        var (kills, jumps) = await StoredAsync();
        Assert.Equal([ThisHour.AddHours(-1), ThisHour], kills.Select(k => k.PeriodEnd));
        Assert.Equal(2, jumps.Count);
    }

    [Fact]
    public async Task Waits_for_esi_to_expire_and_backs_off_on_failures()
    {
        _esi.SetHour(DateTimeOffset.UtcNow.AddMinutes(-20), [], []);

        var next = await Worker.PollAsync(CancellationToken.None);

        // Expires 40 minutes from now, plus the margin.
        Assert.InRange(next, TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(41));

        _esi.Failure = new HttpRequestException("ESI is down");
        Assert.Equal(TimeSpan.FromMinutes(1), await Worker.PollAsync(CancellationToken.None));
        Assert.Equal(TimeSpan.FromMinutes(2), await Worker.PollAsync(CancellationToken.None));

        _esi.Failure = null;
        Assert.InRange(await Worker.PollAsync(CancellationToken.None), TimeSpan.FromMinutes(39), TimeSpan.FromMinutes(41));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(4, 8)]
    [InlineData(5, 15)]
    [InlineData(30, 15)]
    public void Backoff_doubles_up_to_fifteen_minutes(int failures, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), SystemActivityService.Backoff(failures));
    }

    [Fact]
    public void Polls_just_after_the_earlier_expiry_and_never_sooner_than_a_minute()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(TimeSpan.FromMinutes(30) + SystemActivityService.ExpiryMargin, SystemActivityService.NextPoll(now, now.AddMinutes(30), now.AddMinutes(45)));
        Assert.Equal(SystemActivityService.MinInterval, SystemActivityService.NextPoll(now, now.AddSeconds(5), null));
        Assert.Equal(SystemActivityService.DefaultInterval + SystemActivityService.ExpiryMargin, SystemActivityService.NextPoll(now, null, now.AddMinutes(-5)));
    }

    [Fact]
    public async Task Keeps_its_tables_and_migration_history_in_the_intel_schema()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelDbContext>();
        var tables = await db.Database
            .SqlQueryRaw<string>("select table_name as \"Value\" from information_schema.tables where table_schema = 'intel' order by table_name")
            .ToListAsync();

        // Every table of the model, and the migration history, in the intel schema; nothing else there.
        var model = db.Model.GetEntityTypes().Select(e => e.GetTableName()).OfType<string>().Distinct();
        Assert.Equal([.. model.Append("__EFMigrationsHistory").Order(StringComparer.Ordinal)], tables.Order(StringComparer.Ordinal));
        Assert.All(db.Model.GetEntityTypes(), e => Assert.Equal(IntelDbContext.Schema, e.GetSchema()));
    }
}

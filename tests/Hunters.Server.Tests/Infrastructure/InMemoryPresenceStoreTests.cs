using Microsoft.Extensions.Time.Testing;
using NewEdenSoft.Hunters.Server.Infrastructure.Presence;

namespace NewEdenSoft.Hunters.Server.Tests.Infrastructure;

public sealed class InMemoryPresenceStoreTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly InMemoryPresenceStore _store;

    public InMemoryPresenceStoreTests() => _store = new InMemoryPresenceStore(_time);

    [Fact]
    public async Task Returns_members_until_they_expire()
    {
        await _store.SetAsync("fleet:1", "char-a", "Jita", TimeSpan.FromSeconds(30));
        await _store.SetAsync("fleet:1", "char-b", "Amarr", TimeSpan.FromSeconds(90));

        Assert.Equal(2, (await _store.GetAllAsync<string>("fleet:1")).Count);

        _time.Advance(TimeSpan.FromSeconds(60));

        var members = await _store.GetAllAsync<string>("fleet:1");
        Assert.Equal(["char-b"], members.Keys);
        Assert.Null(await _store.GetAsync<string>("fleet:1", "char-a"));
        Assert.Equal("Amarr", await _store.GetAsync<string>("fleet:1", "char-b"));
    }

    [Fact]
    public async Task Setting_again_refreshes_expiry()
    {
        await _store.SetAsync("fleet:1", "char-a", "Jita", TimeSpan.FromSeconds(30));
        _time.Advance(TimeSpan.FromSeconds(20));
        await _store.SetAsync("fleet:1", "char-a", "Perimeter", TimeSpan.FromSeconds(30));
        _time.Advance(TimeSpan.FromSeconds(20));

        Assert.Equal("Perimeter", await _store.GetAsync<string>("fleet:1", "char-a"));
    }

    [Fact]
    public async Task Scopes_are_independent()
    {
        await _store.SetAsync("fleet:1", "char-a", 1, TimeSpan.FromMinutes(1));
        await _store.SetAsync("fleet:2", "char-a", 2, TimeSpan.FromMinutes(1));

        await _store.RemoveScopeAsync("fleet:1");

        Assert.Empty(await _store.GetAllAsync<int>("fleet:1"));
        Assert.Equal(2, await _store.GetAsync<int>("fleet:2", "char-a"));
    }

    [Fact]
    public async Task Remove_deletes_single_member()
    {
        await _store.SetAsync("kills:system:30000142", "conn-1", true, TimeSpan.FromMinutes(1));
        await _store.SetAsync("kills:system:30000142", "conn-2", true, TimeSpan.FromMinutes(1));

        await _store.RemoveAsync("kills:system:30000142", "conn-1");

        Assert.Equal(["conn-2"], (await _store.GetAllAsync<bool>("kills:system:30000142")).Keys);
    }

    [Fact]
    public async Task Purge_removes_expired_members()
    {
        await _store.SetAsync("fleet:1", "char-a", "Jita", TimeSpan.FromSeconds(10));
        _time.Advance(TimeSpan.FromSeconds(11));

        _store.PurgeExpired();

        Assert.Empty(await _store.GetAllAsync<string>("fleet:1"));
    }
}

using Microsoft.Extensions.Caching.Memory;
using NewEdenSoft.Hunters.Server.Infrastructure.Caching;

namespace NewEdenSoft.Hunters.Server.Tests.Infrastructure;

public sealed class InMemoryCacheStoreTests : IDisposable
{
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly InMemoryCacheStore _store;

    public InMemoryCacheStoreTests() => _store = new InMemoryCacheStore(_memoryCache);

    public void Dispose() => _memoryCache.Dispose();

    [Fact]
    public async Task Set_get_and_remove()
    {
        await _store.SetAsync("key", "value", TimeSpan.FromMinutes(1));
        Assert.Equal("value", await _store.GetAsync<string>("key"));

        await _store.RemoveAsync("key");
        Assert.Null(await _store.GetAsync<string>("key"));
    }

    [Fact]
    public async Task GetOrCreate_runs_factory_once_for_concurrent_callers()
    {
        var calls = 0;
        var release = new TaskCompletionSource();

        async ValueTask<int> Factory(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await release.Task;
            return 42;
        }

        var callers = Enumerable.Range(0, 10)
            .Select(_ => _store.GetOrCreateAsync("answer", Factory, TimeSpan.FromMinutes(1)).AsTask())
            .ToArray();

        release.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.All(results, r => Assert.Equal(42, r));
        Assert.Equal(1, calls);
    }
}

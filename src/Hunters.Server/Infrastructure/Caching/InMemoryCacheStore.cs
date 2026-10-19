using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Caching;

/// <summary>Single-instance <see cref="ICacheStore"/> backed by <see cref="IMemoryCache"/>.</summary>
public sealed class InMemoryCacheStore(IMemoryCache cache) : ICacheStore
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _createLocks = new();

    public ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(cache.TryGetValue(key, out T? value) ? value : default);

    public ValueTask SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        cache.Set(key, value, timeToLive);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cache.Remove(key);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(key, out T? existing))
            return existing!;

        var gate = _createLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(key, out existing))
                return existing!;

            var value = await factory(cancellationToken);
            cache.Set(key, value, timeToLive);
            return value;
        }
        finally
        {
            gate.Release();
            _createLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, gate));
        }
    }
}

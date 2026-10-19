namespace NewEdenSoft.Hunters.Server.Infrastructure.Caching;

/// <summary>
/// Key/value cache with expiry. Implementations may be local or distributed, so values must be
/// serializable and treated as immutable once stored.
/// </summary>
public interface ICacheStore
{
    ValueTask<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    ValueTask SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the cached value, or creates, stores and returns it. Concurrent callers for the same
    /// key run the factory only once.
    /// </summary>
    ValueTask<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        TimeSpan timeToLive,
        CancellationToken cancellationToken = default);
}

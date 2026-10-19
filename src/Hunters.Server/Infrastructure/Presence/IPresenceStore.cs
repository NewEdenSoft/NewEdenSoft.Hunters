namespace NewEdenSoft.Hunters.Server.Infrastructure.Presence;

/// <summary>
/// Short-lived, high-churn state grouped by scope, for example the live positions of a fleet
/// session's members (<c>fleet:{id}</c> → character id → position) or the connections subscribed
/// to a system's kills (<c>kills:system:{id}</c> → connection id). Each member expires on its own.
/// Values must be serializable so a distributed implementation can replace the in-memory one.
/// </summary>
public interface IPresenceStore
{
    ValueTask SetAsync<T>(string scope, string member, T value, TimeSpan timeToLive, CancellationToken cancellationToken = default);

    ValueTask<T?> GetAsync<T>(string scope, string member, CancellationToken cancellationToken = default);

    /// <summary>All unexpired members of a scope.</summary>
    ValueTask<IReadOnlyDictionary<string, T>> GetAllAsync<T>(string scope, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string scope, string member, CancellationToken cancellationToken = default);

    ValueTask RemoveScopeAsync(string scope, CancellationToken cancellationToken = default);
}

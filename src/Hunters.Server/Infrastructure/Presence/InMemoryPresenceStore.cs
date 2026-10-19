using System.Collections.Concurrent;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Presence;

/// <summary>
/// Single-instance <see cref="IPresenceStore"/>. Expired members are dropped when read and by
/// <see cref="PurgeExpired"/>, which <see cref="PresenceSweeper"/> calls periodically.
/// </summary>
public sealed class InMemoryPresenceStore(TimeProvider timeProvider) : IPresenceStore
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Entry>> _scopes = new();

    public ValueTask SetAsync<T>(string scope, string member, T value, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        var members = _scopes.GetOrAdd(scope, static _ => new ConcurrentDictionary<string, Entry>());
        members[member] = new Entry(value, timeProvider.GetUtcNow() + timeToLive);
        return ValueTask.CompletedTask;
    }

    public ValueTask<T?> GetAsync<T>(string scope, string member, CancellationToken cancellationToken = default)
    {
        if (_scopes.TryGetValue(scope, out var members)
            && members.TryGetValue(member, out var entry)
            && !IsExpired(members, member, entry, timeProvider.GetUtcNow()))
        {
            return ValueTask.FromResult((T?)entry.Value);
        }

        return ValueTask.FromResult(default(T));
    }

    public ValueTask<IReadOnlyDictionary<string, T>> GetAllAsync<T>(string scope, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, T>();
        if (_scopes.TryGetValue(scope, out var members))
        {
            var now = timeProvider.GetUtcNow();
            foreach (var (member, entry) in members)
            {
                if (!IsExpired(members, member, entry, now))
                    result[member] = (T)entry.Value!;
            }
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, T>>(result);
    }

    public ValueTask RemoveAsync(string scope, string member, CancellationToken cancellationToken = default)
    {
        if (_scopes.TryGetValue(scope, out var members))
            members.TryRemove(member, out _);
        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveScopeAsync(string scope, CancellationToken cancellationToken = default)
    {
        _scopes.TryRemove(scope, out _);
        return ValueTask.CompletedTask;
    }

    /// <summary>Removes expired members and empty scopes.</summary>
    public void PurgeExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (scope, members) in _scopes)
        {
            foreach (var (member, entry) in members)
                IsExpired(members, member, entry, now);

            if (members.IsEmpty)
                _scopes.TryRemove(new KeyValuePair<string, ConcurrentDictionary<string, Entry>>(scope, members));
        }
    }

    private static bool IsExpired(ConcurrentDictionary<string, Entry> members, string member, Entry entry, DateTimeOffset now)
    {
        if (entry.ExpiresAt > now)
            return false;

        // Only remove this exact entry; a concurrent SetAsync may have refreshed it.
        members.TryRemove(new KeyValuePair<string, Entry>(member, entry));
        return true;
    }

    private sealed record Entry(object? Value, DateTimeOffset ExpiresAt);
}

using System.Collections.Concurrent;
using NewEdenKit.ESI.Services;

namespace NewEdenSoft.Hunters.Client.Tracking;

/// <summary>Looks up names of EVE ids: solar systems, stations, types and so on.</summary>
public interface IEveNameSource
{
    /// <summary>Names of the ids ESI knows; the others are left out.</summary>
    Task<IReadOnlyDictionary<long, string>> GetNamesAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken);
}

/// <summary><see cref="IEveNameSource"/> through ESI's public <c>POST /universe/names</c>.</summary>
public sealed class EsiNameSource(IUniverse universe) : IEveNameSource
{
    public async Task<IReadOnlyDictionary<long, string>> GetNamesAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken)
    {
        var result = await universe.PostUniverseNames(ids).ExecuteAsync(cancellationToken);
        if (!result.IsSuccess)
            throw new HttpRequestException($"ESI name lookup failed ({(int?)result.StatusCode}): {result.Error?.Message}");
        return result.Value!.ToDictionary(n => (long)n.Id, n => n.Name);
    }
}

/// <summary>
/// Names of systems, stations and ship types, cached for the app's lifetime: they never change.
/// Player structures are not public, so their ids must not be passed (ESI rejects the whole request).
/// </summary>
public sealed class EveNames(IEveNameSource source)
{
    private readonly ConcurrentDictionary<long, string> _names = new();

    public string? Get(long id) => _names.GetValueOrDefault(id);

    /// <summary>Looks up the ids not cached yet, in one request. Returns false when the lookup failed; try again later.</summary>
    public async Task<bool> ResolveAsync(IEnumerable<long> ids, CancellationToken cancellationToken = default)
    {
        var missing = ids.Distinct().Where(id => !_names.ContainsKey(id)).ToList();
        if (missing.Count == 0)
            return true;

        try
        {
            foreach (var (id, name) in await source.GetNamesAsync(missing, cancellationToken))
                _names[id] = name;
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}

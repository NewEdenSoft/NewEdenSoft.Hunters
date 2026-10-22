using NewEdenKit.ESI.Abstractions.Results;
using NewEdenKit.ESI.Services;

namespace NewEdenSoft.Hunters.Intel.Activity;

public sealed record SystemKills(int SystemId, int ShipKills, int PodKills, int NpcKills);

public sealed record SystemJumps(int SystemId, int ShipJumps);

/// <summary>
/// One of ESI's hourly activity lists. <paramref name="PeriodEnd"/> is its <c>Last-Modified</c>: the
/// counts cover the hour ending then. <paramref name="Expires"/> is when ESI has the next one.
/// </summary>
public sealed record HourlyActivity<T>(IReadOnlyList<T> Systems, DateTimeOffset PeriodEnd, DateTimeOffset? Expires);

/// <summary>ESI's public hourly kills and jumps per solar system (known space only).</summary>
public interface ISystemActivitySource
{
    Task<HourlyActivity<SystemKills>> GetKillsAsync(CancellationToken cancellationToken);

    Task<HourlyActivity<SystemJumps>> GetJumpsAsync(CancellationToken cancellationToken);
}

/// <summary><see cref="ISystemActivitySource"/> through NewEdenKit.ESI. Failures throw <see cref="HttpRequestException"/>.</summary>
public sealed class EsiSystemActivitySource(IUniverse universe) : ISystemActivitySource
{
    public async Task<HourlyActivity<SystemKills>> GetKillsAsync(CancellationToken cancellationToken) =>
        Map(await universe.GetUniverseSystemKills().ExecuteAsync(cancellationToken),
            k => new SystemKills((int)k.SystemId, (int)k.ShipKills, (int)k.PodKills, (int)k.NpcKills));

    public async Task<HourlyActivity<SystemJumps>> GetJumpsAsync(CancellationToken cancellationToken) =>
        Map(await universe.GetUniverseSystemJumps().ExecuteAsync(cancellationToken),
            j => new SystemJumps((int)j.SystemId, (int)j.ShipJumps));

    private static HourlyActivity<TOut> Map<TIn, TOut>(EsiResult<IReadOnlyList<TIn>> result, Func<TIn, TOut> select)
    {
        if (!result.IsSuccess)
            throw new HttpRequestException($"ESI answered {(int?)result.StatusCode}: {result.Error?.Message}");
        if (result.Headers.LastModified is not { } periodEnd)
            throw new HttpRequestException("ESI sent no Last-Modified, so the hour the counts cover is unknown.");
        return new HourlyActivity<TOut>([.. result.Value!.Select(select)], periodEnd, result.Headers.Expires);
    }
}

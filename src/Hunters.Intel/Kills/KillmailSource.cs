using System.Net;
using NewEdenKit.ESI.Services;

namespace NewEdenSoft.Hunters.Intel.Kills;

/// <summary>The fitted types of a killmail's victim and how many of each (see <see cref="KillFeedFile.Fittings"/>).</summary>
public sealed record KillmailFittings(int[] Types, int[] Counts);

/// <summary>Full killmails, fetched again for what the slim stored kills don't keep.</summary>
public interface IKillmailSource
{
    /// <summary>
    /// The victim's fittings, or null when ESI doesn't have the killmail (or the hash is wrong). ESI asking
    /// to slow down throws <see cref="EsiThrottledException"/>; other failures throw <see cref="HttpRequestException"/>.
    /// </summary>
    Task<KillmailFittings?> GetFittingsAsync(long killmailId, string hash, CancellationToken cancellationToken);
}

/// <summary>ESI told the caller to wait (420 or 429, or few errors left in its error limit) for <see cref="RetryAfter"/>.</summary>
public sealed class EsiThrottledException(TimeSpan retryAfter, string reason) : Exception(reason)
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>
/// ESI's <c>GET /killmails/{id}/{hash}/</c>, through NewEdenKit.ESI. Killmails never change, so one is
/// fetched once. ESI stops answering an address that makes too many errors in a minute: when its
/// error limit runs low, the caller is told to wait until it resets.
/// </summary>
public sealed class EsiKillmailSource(IKillmails killmails) : IKillmailSource
{
    /// <summary>Errors left in ESI's window below which the caller waits for the reset.</summary>
    public const int MinErrorsLeft = 20;

    private static readonly TimeSpan DefaultWait = TimeSpan.FromMinutes(1);

    public async Task<KillmailFittings?> GetFittingsAsync(long killmailId, string hash, CancellationToken cancellationToken)
    {
        var result = await killmails.GetKillmailsKillmailIdKillmailHash(killmailId, hash).ExecuteAsync(cancellationToken);
        var headers = result.Headers;

        if (result.StatusCode is (HttpStatusCode)420 or HttpStatusCode.TooManyRequests)
            throw new EsiThrottledException(headers.RetryAfter ?? headers.ErrorLimitReset ?? DefaultWait, $"ESI answered {(int?)result.StatusCode}.");

        if (result.IsSuccess)
        {
            var items = (result.Value!.Victim.Items ?? []).Select(i =>
                new FeedItem((int)i.ItemTypeId, (int)i.Flag, i.QuantityDestroyed ?? 0, i.QuantityDropped ?? 0));
            var (types, counts) = KillFeedFile.Fittings(items);
            return new KillmailFittings(types, counts);
        }

        // Only errors use up the error limit. Near its end, even a killmail ESI doesn't have waits: it is
        // asked again later rather than given up on.
        if (headers.ErrorLimitRemain < MinErrorsLeft)
            throw new EsiThrottledException(headers.ErrorLimitReset ?? DefaultWait, $"ESI's error limit is down to {headers.ErrorLimitRemain}.");
        if (result.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest))
            throw new HttpRequestException($"ESI answered {(int?)result.StatusCode}: {result.Error?.Message}");
        return null;
    }
}

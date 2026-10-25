using NewEdenKit.ESI.Models.Sovereignty;
using NewEdenKit.ESI.Services;

namespace NewEdenSoft.Hunters.Intel.Sovereignty;

/// <summary>An alliance's claim on a system.</summary>
public sealed record SovereignClaim(int SystemId, long AllianceId, long CorporationId, DateTimeOffset? ClaimedSince);

/// <summary>The alliance claims, and when ESI has the next list.</summary>
public sealed record SovereigntyList(IReadOnlyList<SovereignClaim> Claims, DateTimeOffset? Expires);

/// <summary>Who holds sovereignty where.</summary>
public interface ISovereigntySource
{
    /// <summary>Every system an alliance holds (not NPC factions, not unclaimed ones). Failures throw <see cref="HttpRequestException"/>.</summary>
    Task<SovereigntyList> GetAsync(CancellationToken cancellationToken);
}

/// <summary>ESI's <c>GET /sovereignty/systems</c>, through NewEdenKit.ESI.</summary>
public sealed class EsiSovereigntySource(ISovereignty sovereignty) : ISovereigntySource
{
    public async Task<SovereigntyList> GetAsync(CancellationToken cancellationToken)
    {
        var result = await sovereignty.GetSovereigntySystems().ExecuteAsync(cancellationToken);
        if (!result.IsSuccess)
            throw new HttpRequestException($"ESI answered {(int?)result.StatusCode}: {result.Error?.Message}");

        var claims = result.Value!.SolarSystems
            .Select(s => s.Claim is SovereigntySystemsSolarsystem.ClaimModel.Alliance { Value: var a }
                ? new SovereignClaim((int)s.SolarSystemId, a.AllianceId, a.CorporationId, a.ClaimedSince)
                : null)
            .OfType<SovereignClaim>()
            .ToList();
        return new SovereigntyList(claims, result.Headers.Expires);
    }
}

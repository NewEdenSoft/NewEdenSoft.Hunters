using Microsoft.EntityFrameworkCore;
using NewEdenSoft.Hunters.Contracts.Kills;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Server.Features.Hunting;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;

namespace NewEdenSoft.Hunters.Server.Features.Kills;

/// <summary><c>GET /api/kills/systems/{id}</c>: a system's most recent kills from the intel service's kill feed.</summary>
public static class KillEndpoints
{
    public const string UnavailableCode = "kills_unavailable";
    public const string NotFoundCode = "kills_not_found";

    /// <summary>New kills arrive every few seconds somewhere; a system's list is asked again after this.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    public static IEndpointRouteBuilder MapKills(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/kills/systems/{systemId:int}", async (int systemId, int? limit, HttpContext http,
                HuntingStore hunting, MapPackStore maps, IntelDbContext db, CancellationToken cancellationToken) =>
            {
                // The kills come with the hunting aggregates: both need the intel service's kill feed.
                if (hunting.Current is null)
                {
                    http.Response.Headers.RetryAfter = "300";
                    return Results.Problem(title: "No kills yet: the intel service has not read its kill feed.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = UnavailableCode });
                }

                if (maps.Current is not { } map)
                {
                    http.Response.Headers.RetryAfter = "30";
                    return Results.Problem(title: "The map is still being prepared. Try again shortly.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = MapEndpoints.LoadingCode });
                }

                if (limit is < 1 or > KillPaths.MaxLimit)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["limit"] = [$"The limit must be 1–{KillPaths.MaxLimit}."] });

                if (!map.Geometry.IndexOf.ContainsKey(systemId))
                {
                    return Results.Problem(title: "No such system on the map.",
                        statusCode: StatusCodes.Status404NotFound,
                        extensions: new Dictionary<string, object?> { ["code"] = NotFoundCode });
                }

                var kills = await db.Kills.AsNoTracking()
                    .Where(k => k.SystemId == systemId)
                    .OrderByDescending(k => k.Time).ThenByDescending(k => k.KillmailId)
                    .Take(limit ?? KillPaths.DefaultLimit)
                    .ToListAsync(cancellationToken);

                // The newest kill and how many: a new kill, or one aging out of the retention, changes it.
                var etag = $"\"kills-{systemId}-{(kills.Count > 0 ? kills[0].KillmailId : 0)}-{kills.Count}\"";
                if (Revalidation.IsNotModified(http, etag, MaxAge))
                    return Results.StatusCode(StatusCodes.Status304NotModified);

                return Results.Ok(new SystemKills(systemId, [.. kills.Select(ToRecent)]));
            })
            .WithName("GetSystemKills")
            .WithTags("Kills")
            .RequireAuthorization();

        return endpoints;
    }

    public static RecentKill ToRecent(Kill kill) => new(
        kill.KillmailId,
        kill.Hash,
        kill.Time,
        kill.Npc,
        kill.Solo,
        kill.TotalValue,
        new KillParty(kill.VictimCharacterId, kill.VictimCorporationId, kill.VictimAllianceId, kill.VictimShipTypeId),
        FinalBlow(kill),
        kill.AttackerCount);

    /// <summary>The final-blow attacker from the parallel arrays; 0 there means none (an NPC has no pilot).</summary>
    private static KillParty? FinalBlow(Kill kill)
    {
        var i = kill.FinalBlow;
        if (i < 0 || i >= kill.AttackerCharacterIds.Length)
            return null;

        static long? Id(long[] ids, int i) => i < ids.Length && ids[i] != 0 ? ids[i] : null;
        return new KillParty(
            Id(kill.AttackerCharacterIds, i),
            Id(kill.AttackerCorporationIds, i),
            Id(kill.AttackerAllianceIds, i),
            i < kill.AttackerShipTypeIds.Length && kill.AttackerShipTypeIds[i] != 0 ? kill.AttackerShipTypeIds[i] : null);
    }
}

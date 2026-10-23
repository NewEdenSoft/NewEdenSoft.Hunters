using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Contracts.Intel;
using NewEdenSoft.Hunters.Domain.Map;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Server.Features.Activity;
using NewEdenSoft.Hunters.Server.Features.Hunting;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;

namespace NewEdenSoft.Hunters.Server.Features.Intel;

/// <summary>
/// <c>GET /api/intel/reach</c> (docs/decisions.md, D23): the systems in reach of an origin, by the user's
/// own limits, with their activity and hunting intel. The search runs over the in-memory map geometry
/// (about 0.1 ms for the whole map); everything else is lookups in the activity and hunting snapshots.
/// </summary>
public static class ReachEndpoints
{
    public const string NotFoundCode = "reach_origin_not_found";
    public const int DefaultLimit = 200, MaxLimit = 500;

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(1);

    public static IEndpointRouteBuilder MapIntel(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/intel/reach", (int from, int? jumps, double? ly, string? profile, int? limit,
                HttpContext http, MapPackStore maps, ActivityStore activity, HuntingStore hunting, TimeProvider time) =>
            {
                if (maps.Current is not { } map)
                {
                    http.Response.Headers.RetryAfter = "30";
                    return Results.Problem(title: "The map is still being prepared. Try again shortly.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = MapEndpoints.LoadingCode });
                }

                var errors = Validate(jumps, ly, limit, profile, hunting.Current);
                if (errors.Count > 0)
                    return Results.ValidationProblem(errors);

                if (!map.Geometry.IndexOf.TryGetValue(from, out var origin))
                {
                    return Results.Problem(title: "The origin is not a system on the map.",
                        statusCode: StatusCodes.Status404NotFound,
                        extensions: new Dictionary<string, object?> { ["code"] = NotFoundCode });
                }

                var now = time.GetUtcNow();
                var (activityNow, huntingNow) = (activity.Current, hunting.Current);
                var etag = $"\"reach-{map.Build}-{activityNow?.Version ?? 0}-{huntingNow?.Version ?? 0}-{now:yyyyMMddHH}\"";
                if (Revalidation.IsNotModified(http, etag, MaxAge))
                    return Results.StatusCode(StatusCodes.Status304NotModified);

                var rows = Reach.Find(map.Geometry, origin, jumps, ly)
                    .Select(r => Row(map.Geometry, r, activityNow, huntingNow, now.Hour))
                    .ToList();
                var sorted = Sort(rows, profile).Take(limit ?? DefaultLimit).ToList();
                return Results.Ok(new IntelReach(from, jumps, ly, profile, rows.Count, now.Hour, activityNow?.Version, huntingNow?.Version, sorted));
            })
            .WithName("GetIntelReach")
            .WithTags("Intel")
            .RequireAuthorization();

        return endpoints;
    }

    private static Dictionary<string, string[]> Validate(int? jumps, double? ly, int? limit, string? profile, HuntingSnapshot? hunting)
    {
        var errors = new Dictionary<string, string[]>();
        if (jumps is null && ly is null)
            errors["jumps"] = ["Give gate jumps (jumps), a jump-drive range in light years (ly), or both."];
        if (jumps is < Reach.MinJumps or > Reach.MaxJumps)
            errors["jumps"] = [$"Gate jumps must be {Reach.MinJumps}–{Reach.MaxJumps}."];
        if (ly is < Reach.MinLy or > Reach.MaxLy)
            errors["ly"] = [$"Light years must be {Reach.MinLy}–{Reach.MaxLy}."];
        if (limit is < 1 or > MaxLimit)
            errors["limit"] = [$"The limit must be 1–{MaxLimit}."];
        if (profile is not null && hunting is not null && hunting.Profiles.All(p => p.Id != profile))
            errors["profile"] = [$"Unknown hunting profile \"{profile}\"."];
        return errors;
    }

    private static ReachSystem Row(MapGeometry geometry, ReachedSystem reached, ActivitySnapshot? activity, HuntingSnapshot? hunting, int hour)
    {
        var system = geometry.Systems[reached.Index];
        return new ReachSystem(
            system.Id,
            reached.Jumps,
            Math.Round(reached.Ly, 2),
            Math.Round(system.Security, 4),
            reached.CynoTarget,
            activity is null ? null : Activity(activity, system.Id),
            hunting?.Systems.GetValueOrDefault(system.Id) is { } seen ? [.. seen.Select(p => Profile(p, hour))] : []);
    }

    private static ReachActivity Activity(ActivitySnapshot snapshot, int systemId)
    {
        var metrics = ActivitySnapshot.Metrics(snapshot.Systems.GetValueOrDefault(systemId));
        ReachMetric Metric(int m) => metrics?[m] is { } v
            ? new ReachMetric(v.LastHour, v.PreviousHour, v.Day, v.Typical is { } t ? Math.Round(t, 2) : null,
                v.PeakRatio is { } r ? Math.Round(r, 3) : null, v.HoursToPeak)
            // No row: no activity in the last day.
            : new ReachMetric(0, snapshot.HasPrevious(m) ? 0 : null, 0, null, null, null);
        return new ReachActivity(Metric(0), Metric(1), Metric(2), Metric(3));
    }

    /// <summary>"Likely now": the score times this hour's share; "peak now": this hour against the busiest.</summary>
    private static ReachProfile Profile(HuntingPlaceProfile seen, int hour)
    {
        var total = seen.Hours.Sum();
        var busiest = seen.Hours.Count > 0 ? seen.Hours.Max() : 0;
        var thisHour = seen.Hours.Count > hour ? seen.Hours[hour] : 0;
        return new ReachProfile(
            seen.Profile,
            seen.Score,
            seen.LastSeen,
            total > 0 ? Math.Round(seen.Score * thisHour / total, 3) : 0,
            busiest > 0 ? Math.Round(thisHour / busiest, 3) : null);
    }

    /// <summary>By the profile's "likely now" (then its score), or else by the rise in NPC kills; then nearest first.</summary>
    private static IEnumerable<ReachSystem> Sort(List<ReachSystem> rows, string? profile)
    {
        if (profile is not null)
        {
            ReachProfile? Of(ReachSystem r) => r.Profiles.FirstOrDefault(p => p.Profile == profile);
            return rows
                .OrderByDescending(r => Of(r)?.LikelyNow ?? -1)
                .ThenByDescending(r => Of(r)?.Score ?? -1)
                .ThenBy(r => r.Jumps ?? int.MaxValue).ThenBy(r => r.Ly);
        }

        return rows
            .OrderByDescending(r => r.Activity is { NpcKills: var npc } ? npc.LastHour - (npc.PreviousHour ?? npc.LastHour) : 0)
            .ThenByDescending(r => r.Activity?.NpcKills.LastHour ?? 0)
            .ThenBy(r => r.Jumps ?? int.MaxValue).ThenBy(r => r.Ly);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using NewEdenSoft.Hunters.Contracts.Activity;
using NewEdenSoft.Hunters.Intel.Data;
using NewEdenSoft.Hunters.Server.Features.Map;

namespace NewEdenSoft.Hunters.Server.Features.Activity;

public static class ActivityEndpoints
{
    public const string UnavailableCode = "activity_unavailable";
    public const string NotFoundCode = "activity_not_found";

    /// <summary>The intel service usually has a new hour this long after the latest one ended.</summary>
    private static readonly TimeSpan NextHourAfter = TimeSpan.FromMinutes(62);

    public static IEndpointRouteBuilder MapActivity(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/activity").WithTags("Activity").RequireAuthorization();

        // The whole map, precompressed: the same bytes for everyone until the next hour.
        group.MapGet("/current", (HttpContext http, ActivityStore activity, MapPackStore maps, TimeProvider time) =>
            {
                if (Unavailable(http, activity.Current, maps.Current) is { } problem)
                    return problem;

                var response = activity.Response(activity.Current!, maps.Current!);
                if (Cache(http, response.ETag, activity.Current!, time))
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                return response.Body.ToResult(http);
            })
            .WithName("GetCurrentActivity");

        group.MapGet("/systems/{systemId:int}", (int systemId, HttpContext http, ActivityStore activity, MapPackStore maps, IntelDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
                DetailsAsync(http, activity, maps, time, systemId, map => map.Pack.Systems.Any(s => s.Id == systemId),
                    snapshot => ActivitySnapshot.Metrics(snapshot.Systems.GetValueOrDefault(systemId)),
                    db.SystemActivityWeek.Where(w => w.SystemId == systemId)
                        .Select(w => new WeekRow(w.HourOfWeek, w.NpcKills, w.ShipKills, w.PodKills, w.ShipJumps)),
                    cancellationToken))
            .WithName("GetSystemActivity");

        group.MapGet("/regions/{regionId:int}", (int regionId, HttpContext http, ActivityStore activity, MapPackStore maps, IntelDbContext db, TimeProvider time, CancellationToken cancellationToken) =>
                DetailsAsync(http, activity, maps, time, regionId, map => map.Pack.Regions.Any(r => r.Id == regionId),
                    snapshot => ActivitySnapshot.Metrics(snapshot.Regions.GetValueOrDefault(regionId)),
                    db.RegionActivityWeek.Where(w => w.RegionId == regionId)
                        .Select(w => new WeekRow(w.HourOfWeek, w.NpcKills, w.ShipKills, w.PodKills, w.ShipJumps)),
                    cancellationToken))
            .WithName("GetRegionActivity");

        return endpoints;
    }

    private static async Task<IResult> DetailsAsync(
        HttpContext http, ActivityStore activity, MapPackStore maps, TimeProvider time, int id,
        Func<MapPackSnapshot, bool> exists, Func<ActivitySnapshot, ActivityMetric[]?> current,
        IQueryable<WeekRow> week, CancellationToken cancellationToken)
    {
        if (Unavailable(http, activity.Current, maps.Current) is { } problem)
            return problem;
        var snapshot = activity.Current!;

        if (!exists(maps.Current!))
        {
            return Results.Problem(title: "No such system or region on the map.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["code"] = NotFoundCode });
        }

        if (Cache(http, snapshot.ETag, snapshot, time))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        var rows = await week.ToListAsync(cancellationToken);
        var metrics = current(snapshot);
        var header = snapshot.Header;

        // Averages per hour of week: zero where there were samples but no activity, null without samples.
        ActivityMetricDetails Details(int m)
        {
            var value = metrics?[m];
            double?[] averages = new double?[HourOfWeek.Count];
            for (short h = 0; h < HourOfWeek.Count; h++)
            {
                var samples = snapshot.Samples.TryGetValue(h, out var s) ? m == 3 ? s.JumpHours : s.KillHours : 0;
                if (samples > 0)
                    averages[h] = 0;
            }

            foreach (var row in rows)
            {
                var samples = snapshot.Samples.TryGetValue(row.HourOfWeek, out var s) ? m == 3 ? s.JumpHours : s.KillHours : 0;
                if (samples > 0)
                    averages[row.HourOfWeek] = Math.Round((double)row.Sum(m) / samples, 2);
            }

            return new ActivityMetricDetails(
                value?.LastHour ?? 0,
                value?.PreviousHour ?? (snapshot.HasPrevious(m) ? 0 : null),
                value?.Day ?? 0,
                averages[HourOfWeek.Covered(m == 3 ? header.JumpsPeriodEnd : header.KillsPeriodEnd)],
                value?.PeakRatio is { } r ? Math.Round(r, 3) : null,
                value?.HoursToPeak,
                value?.PeakHourOfWeek,
                averages);
        }

        return Results.Ok(new ActivityDetails(
            id,
            snapshot.Version,
            header.KillsPeriodEnd,
            header.JumpsPeriodEnd,
            header.KillsDayHours,
            header.JumpsDayHours,
            HourOfWeek.Following(header.KillsPeriodEnd),
            Details(0), Details(1), Details(2), Details(3)));
    }

    private sealed record WeekRow(short HourOfWeek, long NpcKills, long ShipKills, long PodKills, long ShipJumps)
    {
        public long Sum(int metric) => metric switch { 0 => NpcKills, 1 => ShipKills, 2 => PodKills, _ => ShipJumps };
    }

    /// <summary>503 until both the map pack and the first activity hour are in.</summary>
    private static IResult? Unavailable(HttpContext http, ActivitySnapshot? activity, MapPackSnapshot? map)
    {
        if (map is null)
        {
            http.Response.Headers.RetryAfter = "30";
            return Results.Problem(title: "The map is still being prepared. Try again shortly.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = MapEndpoints.LoadingCode });
        }

        if (activity is null)
        {
            http.Response.Headers.RetryAfter = "300";
            return Results.Problem(title: "No activity data yet: the intel service has not stored its first hour.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = UnavailableCode });
        }

        return null;
    }

    /// <summary>
    /// Sets the ETag and a lifetime up to when the next hour is expected; returns whether the client's
    /// copy is still current.
    /// </summary>
    private static bool Cache(HttpContext http, string etag, ActivitySnapshot snapshot, TimeProvider time)
    {
        var untilNext = snapshot.Header.KillsPeriodEnd + NextHourAfter - time.GetUtcNow();
        var maxAge = (int)Math.Clamp(untilNext.TotalSeconds, 0, 3600);
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = $"private, max-age={maxAge}";

        return http.Request.GetTypedHeaders().IfNoneMatch.Any(e => e.Tag.Value == etag || e.Equals(EntityTagHeaderValue.Any));
    }
}

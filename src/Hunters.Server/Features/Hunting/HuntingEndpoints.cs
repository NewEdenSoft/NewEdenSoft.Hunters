using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;

namespace NewEdenSoft.Hunters.Server.Features.Hunting;

public static class HuntingEndpoints
{
    public const string UnavailableCode = "hunting_unavailable";
    public const string NotFoundCode = "hunting_not_found";

    /// <summary>The aggregates change at most every few minutes; clients revalidate after this.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(1);

    public static IEndpointRouteBuilder MapHunting(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/hunting").WithTags("Hunting").RequireAuthorization();

        group.MapGet("/profiles", (HttpContext http, HuntingStore hunting) =>
                hunting.Current is { } snapshot
                    ? Revalidation.IsNotModified(http, snapshot.ETag, MaxAge) ? Results.StatusCode(StatusCodes.Status304NotModified) : Results.Ok(snapshot.Profiles)
                    : Unavailable(http))
            .WithName("GetHuntingProfiles");

        // Every profile's systems, precompressed: the same bytes for everyone until the next version.
        group.MapGet("/overview", (HttpContext http, HuntingStore hunting) =>
                hunting.Current is { } snapshot
                    ? Revalidation.IsNotModified(http, snapshot.ETag, MaxAge) ? Results.StatusCode(StatusCodes.Status304NotModified) : snapshot.Overview.ToResult(http)
                    : Unavailable(http))
            .WithName("GetHuntingOverview");

        group.MapGet("/systems/{systemId:int}", (int systemId, HttpContext http, HuntingStore hunting, MapPackStore maps) =>
                Place(http, hunting, maps, systemId, map => map.Pack.Systems.Any(s => s.Id == systemId), s => s.Systems))
            .WithName("GetSystemHunting");

        group.MapGet("/regions/{regionId:int}", (int regionId, HttpContext http, HuntingStore hunting, MapPackStore maps) =>
                Place(http, hunting, maps, regionId, map => map.Pack.Regions.Any(r => r.Id == regionId), s => s.Regions))
            .WithName("GetRegionHunting");

        return endpoints;
    }

    private static IResult Place(
        HttpContext http, HuntingStore hunting, MapPackStore maps, int id,
        Func<MapPackSnapshot, bool> exists, Func<HuntingSnapshot, IReadOnlyDictionary<int, IReadOnlyList<HuntingPlaceProfile>>> places)
    {
        if (hunting.Current is not { } snapshot)
            return Unavailable(http);

        if (maps.Current is not { } map)
        {
            http.Response.Headers.RetryAfter = "30";
            return Results.Problem(title: "The map is still being prepared. Try again shortly.",
                statusCode: StatusCodes.Status503ServiceUnavailable,
                extensions: new Dictionary<string, object?> { ["code"] = MapEndpoints.LoadingCode });
        }

        if (!exists(map))
        {
            return Results.Problem(title: "No such system or region on the map.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?> { ["code"] = NotFoundCode });
        }

        if (Revalidation.IsNotModified(http, snapshot.ETag, MaxAge))
            return Results.StatusCode(StatusCodes.Status304NotModified);
        return Results.Ok(new HuntingPlace(id, snapshot.Version, snapshot.UpdatedAt, places(snapshot).GetValueOrDefault(id) ?? []));
    }

    /// <summary>503 until the intel service has computed the aggregates once.</summary>
    private static IResult Unavailable(HttpContext http)
    {
        http.Response.Headers.RetryAfter = "300";
        return Results.Problem(title: "No hunting data yet: the intel service has not computed its first aggregates.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            extensions: new Dictionary<string, object?> { ["code"] = UnavailableCode });
    }
}

public static class HuntingFeature
{
    /// <summary>Serves the intel service's hunting aggregates (the <c>IntelDbContext</c> comes with the activity feature).</summary>
    public static IServiceCollection AddHuntingFeature(this IServiceCollection services)
    {
        services.AddSingleton<HuntingStore>();
        services.AddSingleton<HuntingService>();
        services.AddHostedService(sp => sp.GetRequiredService<HuntingService>());
        return services;
    }
}

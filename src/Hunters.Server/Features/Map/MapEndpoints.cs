using NewEdenSoft.Hunters.Contracts.Map;

namespace NewEdenSoft.Hunters.Server.Features.Map;

public static class MapEndpoints
{
    public const string LoadingCode = "map_pack_loading";
    public const string NotFoundCode = "map_pack_not_found";

    /// <summary>Route of <see cref="MapPaths.Pack"/>.</summary>
    public const string PackRoute = "/api/map/packs/{sdeBuild:long}";

    public static IEndpointRouteBuilder MapMap(this IEndpointRouteBuilder endpoints)
    {
        // Behind login like everything else (docs/decisions.md, D19). Immutable per build, so clients
        // download it once; the discovery document says which build is current.
        endpoints.MapGet(PackRoute, (long sdeBuild, HttpContext http, MapPackStore store) =>
            {
                if (store.Current is not { } pack)
                {
                    http.Response.Headers.RetryAfter = "30";
                    return Results.Problem(title: "The map is still being prepared. Try again shortly.",
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        extensions: new Dictionary<string, object?> { ["code"] = LoadingCode });
                }

                if (pack.Build != sdeBuild)
                {
                    return Results.Problem(title: "This map version is not available; the discovery document names the current one.",
                        statusCode: StatusCodes.Status404NotFound,
                        extensions: new Dictionary<string, object?> { ["code"] = NotFoundCode });
                }

                // Served precompressed: the pack is the same bytes for everyone.
                http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
                return pack.Body.ToResult(http);
            })
            .WithName("GetMapPack")
            .WithTags("Map")
            .RequireAuthorization();

        return endpoints;
    }
}

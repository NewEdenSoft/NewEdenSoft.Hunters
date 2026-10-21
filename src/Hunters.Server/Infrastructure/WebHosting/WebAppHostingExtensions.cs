namespace NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;

public static class WebAppHostingExtensions
{
    // Client-side routes fall back to index.html; server paths never do, so unknown API
    // routes return 404 instead of the web app.
    private const string FallbackPattern =
        @"{*path:nonfile:regex(^(?!(api|auth|hubs|openapi|\.well-known)(/|$)).*$)}";

    public static WebApplication UseWebApp(this WebApplication app)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        return app;
    }

    public static IEndpointRouteBuilder MapWebAppFallback(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFallbackToFile(FallbackPattern, "index.html");
        return endpoints;
    }
}

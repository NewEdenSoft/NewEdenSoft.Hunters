using Microsoft.Net.Http.Headers;

namespace NewEdenSoft.Hunters.Server.Infrastructure.WebHosting;

/// <summary>ETag caching for responses derived from a data version (the intel endpoints).</summary>
public static class Revalidation
{
    /// <summary>
    /// Sets the ETag and how long the client may use its copy without asking; returns whether the
    /// client's copy is still current (answer 304).
    /// </summary>
    public static bool IsNotModified(HttpContext http, string etag, TimeSpan maxAge)
    {
        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = $"private, max-age={(int)Math.Clamp(maxAge.TotalSeconds, 0, 3600)}";
        return http.Request.GetTypedHeaders().IfNoneMatch.Any(e => e.Tag.Value == etag || e.Equals(EntityTagHeaderValue.Any));
    }
}

using System.Net;
using System.Text.Json;

namespace NewEdenSoft.Hunters.Server.Tests.Infrastructure.WebHosting;

public sealed class OpenApiTests : IDisposable
{
    private readonly HuntersServerFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task The_document_describes_the_api_with_bearer_session_tokens()
    {
        using var document = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync("/openapi/v1.json"));
        var root = document.RootElement;

        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal(("http", "bearer"), (bearer.GetProperty("type").GetString(), bearer.GetProperty("scheme").GetString()));
        Assert.True(root.GetProperty("security")[0].TryGetProperty("Bearer", out _));

        var paths = root.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/activity/current", paths);
        Assert.Contains("/api/map/packs/{sdeBuild}", paths);
    }

    [Fact]
    public async Task The_interactive_reference_is_served_at_scalar()
    {
        var response = await _factory.CreateClient().GetAsync("/scalar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Hunters API", html);
        Assert.Contains("openapi/v1.json", html);

        // Its script is bundled with the server, not loaded from a CDN.
        var script = System.Text.RegularExpressions.Regex.Match(html, "<script[^>]+src=\"([^\"]+)\"").Groups[1].Value;
        Assert.DoesNotContain("://", script);
        var scriptResponse = await _factory.CreateClient().GetAsync(new Uri(new Uri("http://localhost/scalar/"), script));
        Assert.Equal(HttpStatusCode.OK, scriptResponse.StatusCode);
    }
}

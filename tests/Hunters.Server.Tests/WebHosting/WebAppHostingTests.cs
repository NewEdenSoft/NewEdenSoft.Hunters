using System.Net;
using NewEdenSoft.Hunters.Contracts.Discovery;

namespace NewEdenSoft.Hunters.Server.Tests.WebHosting;

public sealed class WebAppHostingTests
{
    private const string IndexHtml = "<html>hunters-web</html>";

    [Theory]
    [InlineData("/")]
    [InlineData("/map")]
    [InlineData("/account/characters")]
    public async Task Serves_index_for_client_routes_when_enabled(string path)
    {
        using var factory = new HuntersServerFactory().WithWebApp(("index.html", IndexHtml));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(IndexHtml, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Serves_static_assets_when_enabled()
    {
        using var factory = new HuntersServerFactory().WithWebApp(("index.html", IndexHtml), ("assets/app.js", "console.log(1)"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/assets/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("console.log(1)", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/api/unknown")]
    [InlineData("/auth/unknown")]
    [InlineData("/hubs/unknown")]
    [InlineData("/.well-known/unknown")]
    public async Task Server_paths_do_not_fall_back_to_index(string path)
    {
        using var factory = new HuntersServerFactory().WithWebApp(("index.html", IndexHtml));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Discovery_still_works_when_enabled()
    {
        using var factory = new HuntersServerFactory().WithWebApp(("index.html", IndexHtml));
        using var client = factory.CreateClient();

        var response = await client.GetAsync(DiscoveryDocument.Path);

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Does_not_serve_web_app_when_disabled()
    {
        using var factory = new HuntersServerFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/map");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

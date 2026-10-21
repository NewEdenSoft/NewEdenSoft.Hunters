using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Contracts.Discovery;
using NewEdenSoft.Hunters.Contracts.Map;
using NewEdenSoft.Hunters.Server.Features.Map;
using NewEdenSoft.Hunters.Server.Tests.Features.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Map;

[Collection(PostgresCollection.Name)]
public sealed class MapEndpointTests(PostgresFixture postgres) : IDisposable
{
    private readonly HuntersServerFactory _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Discovery_names_the_current_map_pack_once_it_is_built()
    {
        await MapReadyAsync();

        var discovery = await _factory.CreateClient().GetFromJsonAsync<DiscoveryDocument>(DiscoveryDocument.Path);

        Assert.Equal(FakeSdeMap.Build, discovery!.MapPack?.SdeBuild);
    }

    [Fact]
    public async Task Map_pack_requires_a_session()
    {
        await MapReadyAsync();

        var response = await _factory.CreateClient().GetAsync(MapPaths.Pack(FakeSdeMap.Build));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Map_pack_is_served_compressed_and_immutable()
    {
        await MapReadyAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, MapPaths.Pack(FakeSdeMap.Build));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

        var response = await (await SignedInClientAsync()).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("br", Assert.Single(response.Content.Headers.ContentEncoding));
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString());
        await using var body = new BrotliStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        var pack = await JsonSerializer.DeserializeAsync<MapPack>(body, JsonSerializerOptions.Web);
        Assert.Equal(3, pack!.Systems.Count);
        Assert.Equal(2, pack.Gates.Count);
    }

    [Fact]
    public async Task Map_pack_is_plain_json_without_accept_encoding()
    {
        await MapReadyAsync();

        var pack = await (await SignedInClientAsync()).GetFromJsonAsync<MapPack>(MapPaths.Pack(FakeSdeMap.Build));

        Assert.Equal(FakeSdeMap.Build, pack!.SdeBuild);
    }

    [Fact]
    public async Task Other_builds_are_not_found()
    {
        await MapReadyAsync();

        var response = await (await SignedInClientAsync()).GetAsync(MapPaths.Pack(FakeSdeMap.Build - 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(MapEndpoints.NotFoundCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Map_pack_is_unavailable_until_the_sde_is_loaded()
    {
        _factory.Sde.Gate = new TaskCompletionSource();
        var client = await SignedInClientAsync();

        var response = await client.GetAsync(MapPaths.Pack(FakeSdeMap.Build));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Null((await client.GetFromJsonAsync<DiscoveryDocument>(DiscoveryDocument.Path))!.MapPack);

        _factory.Sde.Gate.SetResult();
        await MapReadyAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MapPaths.Pack(FakeSdeMap.Build))).StatusCode);
    }

    private async Task<HttpClient> SignedInClientAsync()
    {
        var driver = new LoginDriver(_factory);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Cartographer"));
        return driver.AuthorizedClient(login.SessionToken);
    }

    /// <summary>The map pack is built in the background after startup.</summary>
    private async Task MapReadyAsync()
    {
        var store = _factory.Services.GetRequiredService<MapPackStore>();
        for (var i = 0; store.Current is null && i < 200; i++)
            await Task.Delay(25);
        Assert.NotNull(store.Current);
    }
}

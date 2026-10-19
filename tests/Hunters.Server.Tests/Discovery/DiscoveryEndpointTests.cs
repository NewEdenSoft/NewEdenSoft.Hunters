using System.Net.Http.Json;
using NewEdenSoft.Hunters.Contracts;
using NewEdenSoft.Hunters.Contracts.Discovery;

namespace NewEdenSoft.Hunters.Server.Tests.Discovery;

public sealed class DiscoveryEndpointTests
{
    [Fact]
    public async Task Returns_server_name_and_api_version()
    {
        using var factory = new HuntersServerFactory().With("Hunters:Name", "Test Server");
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<DiscoveryDocument>(DiscoveryDocument.Path);

        Assert.NotNull(document);
        Assert.Equal("Test Server", document.Name);
        Assert.Equal(HuntersApi.Version, document.ApiVersion);
        Assert.False(document.Admission.RequiresAccessKey);
    }

    [Fact]
    public async Task Reports_access_key_requirement()
    {
        using var factory = new HuntersServerFactory().With("Hunters:Admission:AccessKey:Enabled", "true");
        using var client = factory.CreateClient();

        var document = await client.GetFromJsonAsync<DiscoveryDocument>(DiscoveryDocument.Path);

        Assert.True(document!.Admission.RequiresAccessKey);
    }

    [Fact]
    public async Task Uses_camel_case_json()
    {
        using var factory = new HuntersServerFactory();
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync(DiscoveryDocument.Path);

        Assert.Contains("\"apiVersion\"", json);
        Assert.Contains("\"requiresAccessKey\"", json);
    }
}

using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Contracts;
using NewEdenSoft.Hunters.Contracts.Discovery;
using NewEdenSoft.Hunters.Server.Configuration;

namespace NewEdenSoft.Hunters.Server.Discovery;

public static class DiscoveryEndpoints
{
    /// <summary>Oldest desktop client version this server accepts.</summary>
    public const string MinClientVersion = "0.1.0";

    public static IEndpointRouteBuilder MapDiscovery(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(DiscoveryDocument.Path, (IOptionsSnapshot<HuntersOptions> options) =>
            {
                var hunters = options.Value;
                return new DiscoveryDocument(
                    Name: hunters.Name,
                    ApiVersion: HuntersApi.Version,
                    MinClientVersion: MinClientVersion,
                    Admission: new AdmissionInfo(RequiresAccessKey: hunters.Admission.AccessKey.Enabled),
                    MapPack: null,
                    Features: []);
            })
            .WithName("GetDiscoveryDocument")
            .AllowAnonymous();

        return endpoints;
    }
}

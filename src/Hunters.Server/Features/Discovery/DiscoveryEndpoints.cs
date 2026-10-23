using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Contracts;
using NewEdenSoft.Hunters.Contracts.Discovery;
using NewEdenSoft.Hunters.Server.Configuration;
using NewEdenSoft.Hunters.Server.Features.Activity;
using NewEdenSoft.Hunters.Server.Features.Hunting;
using NewEdenSoft.Hunters.Server.Features.Map;

namespace NewEdenSoft.Hunters.Server.Features.Discovery;

public static class DiscoveryEndpoints
{
    /// <summary>Oldest desktop client version this server accepts.</summary>
    public const string MinClientVersion = "0.1.0";

    public static IEndpointRouteBuilder MapDiscovery(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(DiscoveryDocument.Path, (IOptionsSnapshot<HuntersOptions> options, MapPackStore maps, ActivityStore activity, HuntingStore hunting) =>
            {
                var hunters = options.Value;
                return new DiscoveryDocument(
                    Name: hunters.Name,
                    ApiVersion: HuntersApi.Version,
                    MinClientVersion: MinClientVersion,
                    Admission: new AdmissionInfo(RequiresAccessKey: hunters.Admission.AccessKey.Enabled),
                    MapPack: maps.Current is { } pack ? new MapPackInfo(pack.Build) : null,
                    Features: [
                        .. activity.Current is null ? Array.Empty<string>() : [HuntersFeatures.Activity],
                        .. hunting.Current is null ? Array.Empty<string>() : [HuntersFeatures.Hunting],
                    ]);
            })
            .WithName("GetDiscoveryDocument")
            .AllowAnonymous();

        return endpoints;
    }
}

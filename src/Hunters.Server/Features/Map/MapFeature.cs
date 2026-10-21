using NewEdenSoft.Hunters.Server.Infrastructure.Sde;

namespace NewEdenSoft.Hunters.Server.Features.Map;

public static class MapFeature
{
    /// <summary>The map pack, built from the SDE in the background and served to clients.</summary>
    public static IServiceCollection AddMapFeature(this IServiceCollection services)
    {
        services.AddSde();
        services.AddSingleton<MapPackStore>();
        services.AddHostedService<MapPackService>();
        return services;
    }
}

using NewEdenSoft.Hunters.Sde;

namespace NewEdenSoft.Hunters.Server.Features.Map;

public static class MapFeature
{
    /// <summary>The map pack, built from the SDE in the background and served to clients.</summary>
    public static IServiceCollection AddMapFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSde(o =>
        {
            o.CachePath = configuration["Hunters:Sde:CachePath"];
            o.Contact = configuration["Hunters:Esi:Contact"] ?? o.Contact;
        });
        services.AddSingleton<MapPackStore>();
        services.AddHostedService<MapPackService>();
        return services;
    }
}

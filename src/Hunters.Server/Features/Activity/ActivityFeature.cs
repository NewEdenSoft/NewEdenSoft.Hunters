using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Features.Activity;

public static class ActivityFeature
{
    /// <summary>Public activity data collected by the server: ESI's hourly kills and jumps per system.</summary>
    public static IServiceCollection AddActivityFeature(this IServiceCollection services)
    {
        services.AddSingleton<ISystemActivitySource, EsiSystemActivitySource>();
        services.AddHostedService<SystemActivityService>();
        return services;
    }
}

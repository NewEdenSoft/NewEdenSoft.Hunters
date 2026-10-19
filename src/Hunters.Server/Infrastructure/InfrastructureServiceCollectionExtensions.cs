using NewEdenSoft.Hunters.Server.Infrastructure.Caching;
using NewEdenSoft.Hunters.Server.Infrastructure.Messaging;
using NewEdenSoft.Hunters.Server.Infrastructure.Presence;

namespace NewEdenSoft.Hunters.Server.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers the single-instance cache, presence and message bus implementations.</summary>
    public static IServiceCollection AddInMemoryInfrastructure(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.AddSingleton(TimeProvider.System);

        services.AddSingleton<ICacheStore, InMemoryCacheStore>();

        services.AddSingleton<InMemoryPresenceStore>();
        services.AddSingleton<IPresenceStore>(sp => sp.GetRequiredService<InMemoryPresenceStore>());
        services.AddHostedService<PresenceSweeper>();

        services.AddSingleton<IMessageBus, InProcessMessageBus>();

        return services;
    }
}

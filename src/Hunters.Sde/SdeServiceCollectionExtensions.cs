using Microsoft.Extensions.DependencyInjection;

namespace NewEdenSoft.Hunters.Sde;

public static class SdeServiceCollectionExtensions
{
    /// <summary>The SDE map and type sources, sharing one download cache.</summary>
    public static IServiceCollection AddSde(this IServiceCollection services, Action<SdeOptions> configure)
    {
        services.Configure(configure);

        // The archive is about 100 MB: a long timeout, and no service-defaults resilience handler,
        // whose total request timeout would cut the download short.
#pragma warning disable EXTEXP0001 // Experimental API; needed to opt this client out of the service-defaults resilience policy.
        services.AddHttpClient(SdeSource.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(30))
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        services.AddSingleton<SdeSource>();
        services.AddSingleton<ISdeMapSource>(sp => sp.GetRequiredService<SdeSource>());
        services.AddSingleton<ISdeTypeSource>(sp => sp.GetRequiredService<SdeSource>());
        return services;
    }
}

namespace NewEdenSoft.Hunters.Server.Infrastructure.Sde;

public static class SdeServiceCollectionExtensions
{
    public static IServiceCollection AddSde(this IServiceCollection services)
    {
        // The archive is about 100 MB: a long timeout, and no service-defaults resilience handler,
        // whose total request timeout would cut the download short.
#pragma warning disable EXTEXP0001 // Experimental API; needed to opt this client out of the service-defaults resilience policy.
        services.AddHttpClient(SdeMapSource.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(30))
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        services.AddSingleton<ISdeMapSource, SdeMapSource>();
        return services;
    }
}

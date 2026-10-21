namespace NewEdenSoft.Hunters.Server.Infrastructure.Eve;

public static class EveServiceCollectionExtensions
{
    /// <summary>EVE SSO token client, signing keys, token encryption and the public ESI services the server uses.</summary>
    public static IServiceCollection AddEveServices(this IServiceCollection services, IConfiguration configuration)
    {
        // No automatic retries: a retried exchange or refresh could consume a single-use code or
        // refresh token twice and lose the character's login.
#pragma warning disable EXTEXP0001 // Experimental API; needed to opt this client out of the service-defaults retry policy.
        services.AddHttpClient<IEveSsoClient, EveSsoClient>().RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        services.AddHttpClient(nameof(EveSigningKeys));
        services.AddSingleton<EveSigningKeys>();
        services.AddSingleton<TokenProtector>();

        // ESI: public endpoints only. Character calls are made by the clients.
        services.AddEveEsi(o =>
        {
            o.ApplicationName = "NewEdenSoft.Hunters";
            o.Contact = configuration["Hunters:Esi:Contact"] ?? "https://github.com/NewEdenSoft/NewEdenSoft.Hunters";
        });
        services.AddScoped<IEveAffiliationService, EsiAffiliationService>();

        return services;
    }
}

namespace NewEdenSoft.Hunters.Server.Features.Accounts;

/// <summary>Accounts, characters and the access tokens clients use for ESI.</summary>
public static class AccountsFeature
{
    public static IServiceCollection AddAccountsFeature(this IServiceCollection services)
    {
        services.AddScoped<AccountService>();
        services.AddScoped<CharacterTokenService>();
        return services;
    }
}

using NewEdenSoft.Hunters.Server.Configuration;

namespace NewEdenSoft.Hunters.Server.Features.Auth;

/// <summary>Logins (desktop and web), app sessions and the authentication schemes.</summary>
public static class AuthFeature
{
    public static IServiceCollection AddAuthFeature(this IServiceCollection services, SsoOptions sso)
    {
        services.AddHuntersAuthentication(sso);
        services.AddScoped<SessionService>();
        services.AddScoped<LoginService>();
        return services;
    }
}

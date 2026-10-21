using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Configuration;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Features.Auth;

public static class HuntersAuth
{
    /// <summary>Policy scheme: bearer token for the desktop app, cookie for the web app.</summary>
    public const string Scheme = "Hunters";
    public const string BearerScheme = "HuntersBearer";
    public const string CookieScheme = "HuntersCookie";

    public const string AccountClaim = "hunters:account";
    public const string SessionClaim = "hunters:session";

    public static ClaimsPrincipal CreatePrincipal(SessionIdentity identity, string authenticationType) =>
        new(new ClaimsIdentity(
            [new Claim(AccountClaim, identity.AccountId.ToString()), new Claim(SessionClaim, identity.SessionId.ToString())],
            authenticationType));

    /// <summary>The caller's session, or null when not signed in.</summary>
    public static SessionIdentity? GetSession(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(AccountClaim), out var account) && Guid.TryParse(user.FindFirstValue(SessionClaim), out var session)
            ? new SessionIdentity(session, account)
            : null;

    public static IServiceCollection AddHuntersAuthentication(this IServiceCollection services, SsoOptions sso)
    {
        services.AddAuthentication(Scheme)
            .AddPolicyScheme(Scheme, "Hunters", o => o.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? BearerScheme
                    : CookieScheme)
            .AddScheme<AuthenticationSchemeOptions, BearerSessionHandler>(BearerScheme, null)
            .AddCookie(CookieScheme, o =>
            {
                o.Cookie.Name = "hunters.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(30);
                o.SlidingExpiration = true;
                o.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = async context =>
                    {
                        var session = context.Principal?.GetSession();
                        var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionService>();
                        if (session is null || !await sessions.IsActiveAsync(session.SessionId, context.HttpContext.RequestAborted))
                        {
                            context.RejectPrincipal();
                            await context.HttpContext.SignOutAsync(CookieScheme);
                        }
                    },
                    // An API: answer 401/403 instead of redirecting to a login page.
                    OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    },
                    OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    },
                };
            })
            // EVE SSO: the standard OAuth handler. Login details travel in its protected state;
            // LoginEvents decide what a completed login means.
            .AddOAuth<OAuthOptions, EveOAuthHandler>(EveOAuthHandler.SchemeName, o =>
            {
                // Placeholders keep the server running without SSO configured; logins then fail at EVE.
                o.ClientId = sso.ClientId is { Length: > 0 } id ? id : "not-configured";
                o.ClientSecret = sso.ClientSecret is { Length: > 0 } secret ? secret : "not-configured";
                o.AuthorizationEndpoint = sso.AuthorizeUrl;
                o.TokenEndpoint = sso.TokenUrl;
                foreach (var scope in sso.Scopes)
                    o.Scope.Add(scope);

                o.CallbackPath = AuthPaths.EveCallback;
                o.SignInScheme = CookieScheme;
                o.SaveTokens = false;
                o.UsePkce = false;
                // The return from EVE is a top-level GET navigation, which Lax cookies survive.
                o.CorrelationCookie.SameSite = SameSiteMode.Lax;
                o.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.Events = LoginEvents.Create();
            });

        services.AddAuthorization();
        return services;
    }
}

/// <summary>Authenticates desktop app sessions from <c>Authorization: Bearer hs_…</c>.</summary>
public sealed class BearerSessionHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    SessionService sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var identity = await sessions.ValidateBearerAsync(header["Bearer ".Length..].Trim(), Context.RequestAborted);
        return identity is null
            ? AuthenticateResult.Fail("Invalid or revoked session token.")
            : AuthenticateResult.Success(new AuthenticationTicket(HuntersAuth.CreatePrincipal(identity, Scheme.Name), Scheme.Name));
    }
}

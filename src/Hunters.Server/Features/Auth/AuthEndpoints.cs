using Microsoft.AspNetCore.Authentication;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Features.Auth;

/// <summary>
/// Login endpoints for the desktop and web apps. Both end in a standard OAuth challenge of the
/// EVE scheme; the shared callback (<see cref="AuthPaths.EveCallback"/>) is handled by the OAuth
/// handler and <see cref="LoginEvents"/>.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("").AllowAnonymous().WithTags("Auth");

        auth.MapPost(AuthPaths.DesktopBegin, async (BeginDesktopLoginRequest request, HttpContext http, LoginService logins) =>
        {
            // With a valid bearer token this adds a character to the signed-in account.
            var current = await CurrentSessionAsync(http);
            var (response, error) = await logins.BeginDesktopAsync(request, current?.AccountId, BaseUrl(http.Request), http.RequestAborted);
            return error is null ? Results.Ok(response) : Problem(error);
        });

        // Opened in the user's browser: binds the login to this browser (correlation cookie) and goes to EVE.
        auth.MapGet(AuthPaths.DesktopBrowser + "{loginId}", async (string loginId, LoginService logins, CancellationToken ct) =>
        {
            var login = await logins.TakeDesktopLoginAsync(loginId, ct);
            return login is not null
                ? Results.Challenge(login.ToProperties(), [EveOAuthHandler.SchemeName])
                : Results.Content(
                    LoginEvents.MessagePageHtml("Login link expired", "This login link has expired or was already used. Start the login again from the Hunters app."),
                    "text/html", statusCode: StatusCodes.Status400BadRequest);
        });

        auth.MapPost(AuthPaths.DesktopRedeem, async (RedeemDesktopLoginRequest request, LoginService logins, CancellationToken ct) =>
        {
            var (result, error) = await logins.RedeemDesktopAsync(request, ct);
            return error is null ? Results.Ok(result) : Problem(error);
        });

        auth.MapMethods(AuthPaths.WebLogin, ["GET", "POST"], async (HttpContext http, LoginService logins) =>
        {
            var form = http.Request.HasFormContentType ? await http.Request.ReadFormAsync(http.RequestAborted) : null;
            var accessKey = form?["accessKey"].ToString();
            var returnUrl = SafeReturnUrl(form?["returnUrl"].ToString() is { Length: > 0 } fromForm ? fromForm : http.Request.Query["returnUrl"].ToString());

            // Signed in already: the login adds a character to the account.
            var current = await CurrentSessionAsync(http);
            var (login, error) = await logins.PrepareWebAsync(accessKey, returnUrl, current?.AccountId, UserAgent(http), http.RequestAborted);
            return error is null
                ? Results.Challenge(login!.ToProperties(), [EveOAuthHandler.SchemeName])
                : Results.Redirect(LoginEvents.WebErrorUrl(error));
        }).DisableAntiforgery();

        auth.MapPost(AuthPaths.WebLogout, async (HttpContext http, SessionService sessions) =>
        {
            if (await CurrentSessionAsync(http) is { } session)
                await sessions.RevokeAsync(session.AccountId, session.SessionId, http.RequestAborted);
            await http.SignOutAsync(HuntersAuth.CookieScheme);
            return Results.Redirect("/");
        }).DisableAntiforgery();

        return endpoints;
    }

    private static async Task<SessionIdentity?> CurrentSessionAsync(HttpContext http)
    {
        var result = await http.AuthenticateAsync(HuntersAuth.Scheme);
        return result.Succeeded ? result.Principal.GetSession() : null;
    }

    private static string BaseUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}";

    private static string? UserAgent(HttpContext http) => http.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null;

    /// <summary>Only local paths, so the login cannot be used as an open redirect.</summary>
    private static string? SafeReturnUrl(string? url) =>
        url is { Length: > 0 } && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : null;

    private static IResult Problem(LoginError error) =>
        Results.Problem(title: error.Message, statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}

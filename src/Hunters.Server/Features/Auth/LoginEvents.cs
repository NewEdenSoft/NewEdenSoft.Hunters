using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Features.Auth;

/// <summary>
/// Events of the EVE OAuth handler. The handler has already validated state and correlation and
/// exchanged the code; these decide what the login means and where the browser goes next: the
/// desktop app's loopback listener or the web app.
/// </summary>
public static class LoginEvents
{
    public static OAuthEvents Create() => new()
    {
        OnTicketReceived = OnTicketReceivedAsync,
        OnAccessDenied = OnAccessDeniedAsync,
        OnRemoteFailure = OnRemoteFailureAsync,
    };

    private static async Task OnTicketReceivedAsync(TicketReceivedContext context)
    {
        context.HandleResponse();
        var http = context.HttpContext;

        var login = LoginRequest.FromProperties(context.Properties);
        var tokens = EveOAuthHandler.GetTokens(http);
        if (login is null || tokens is null)
        {
            await WriteMessagePageAsync(http, "Login failed", "This login could not be completed. Start again.");
            return;
        }

        var logins = http.RequestServices.GetRequiredService<LoginService>();
        var outcome = await logins.CompleteAsync(login, tokens, http.RequestAborted);
        if (!outcome.Succeeded)
        {
            RedirectToError(http, login, outcome.Error!);
            return;
        }

        if (login.Kind == LoginKind.Desktop)
        {
            http.Response.Redirect(await logins.CreateDesktopRedirectAsync(login, outcome, http.RequestAborted));
            return;
        }

        // Adding a character keeps the current web session; a login starts a new one.
        if (login.AccountId is null)
        {
            var sessions = http.RequestServices.GetRequiredService<SessionService>();
            var session = await sessions.CreateWebAsync(outcome.AccountId, login.DeviceName, http.RequestAborted);
            await http.SignInAsync(
                HuntersAuth.CookieScheme,
                HuntersAuth.CreatePrincipal(new SessionIdentity(session.Id, outcome.AccountId), HuntersAuth.CookieScheme),
                new AuthenticationProperties { IsPersistent = true });
        }

        http.Response.Redirect(login.ReturnUrl ?? "/");
    }

    private static Task OnAccessDeniedAsync(AccessDeniedContext context)
    {
        context.HandleResponse();
        return RespondToFailureAsync(context.HttpContext, LoginRequest.FromProperties(context.Properties),
            new LoginError(AuthErrorCodes.LoginCancelled, "The EVE login was cancelled."));
    }

    private static Task OnRemoteFailureAsync(RemoteFailureContext context)
    {
        context.HandleResponse();
        return RespondToFailureAsync(context.HttpContext, LoginRequest.FromProperties(context.Properties),
            new LoginError(AuthErrorCodes.SsoFailed, "EVE Online could not complete the login. Try again."));
    }

    private static Task RespondToFailureAsync(HttpContext http, LoginRequest? login, LoginError error)
    {
        // No readable state, or state from another browser: nowhere safe to send the user back to.
        if (login is null)
            return WriteMessagePageAsync(http, "Login expired",
                "This login has expired or was started in another browser. Start the login again.");

        RedirectToError(http, login, error);
        return Task.CompletedTask;
    }

    private static void RedirectToError(HttpContext http, LoginRequest login, LoginError error) =>
        http.Response.Redirect(login.Kind == LoginKind.Desktop
            ? LoginService.DesktopErrorRedirect(login, error)
            : WebErrorUrl(error));

    public static string WebErrorUrl(LoginError error) =>
        $"/login?error={Uri.EscapeDataString(error.Code)}&message={Uri.EscapeDataString(error.Message)}";

    /// <summary>A minimal standalone page for browser-facing errors outside the web app.</summary>
    public static string MessagePageHtml(string title, string message) => $$"""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}} · Hunters</title>
        <style>
          body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #090d11; color: #e5ecf0; font: 16px/1.5 system-ui, sans-serif; }
          main { max-width: 32rem; padding: 2rem; border: 1px solid #2c3a44; }
          h1 { margin: 0 0 .5rem; font-size: 1.25rem; letter-spacing: .04em; color: #8fb3c9; }
          p { margin: 0; color: #9aa7b0; }
        </style></head>
        <body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(message)}}</p></main></body></html>
        """;

    private static async Task WriteMessagePageAsync(HttpContext http, string title, string message)
    {
        http.Response.StatusCode = StatusCodes.Status400BadRequest;
        http.Response.ContentType = "text/html; charset=utf-8";
        await http.Response.WriteAsync(MessagePageHtml(title, message), http.RequestAborted);
    }
}

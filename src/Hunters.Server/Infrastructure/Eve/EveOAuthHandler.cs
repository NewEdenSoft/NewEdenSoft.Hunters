using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;
using NewEdenSoft.Hunters.Server.Configuration;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Eve;

/// <summary>
/// The standard ASP.NET Core OAuth handler (state protection, correlation cookie, callback,
/// error handling) with two EVE-specific changes:
/// <list type="bullet">
/// <item>The redirect URI is always the configured callback URL, which must match the EVE
/// application exactly, instead of one derived from the request host (proxies change it).</item>
/// <item>The code exchange goes through <see cref="IEveSsoClient"/>: HTTP Basic client
/// authentication and validation of the signed access token.</item>
/// </list>
/// What happens after a successful exchange is decided by the events in <c>LoginEvents</c>.
/// </summary>
public sealed class EveOAuthHandler(
    IOptionsMonitor<OAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IEveSsoClient sso,
    IOptions<HuntersOptions> hunters)
    : OAuthHandler<OAuthOptions>(options, logger, encoder)
{
    public const string SchemeName = "Eve";

    private const string TokensItem = "hunters.eve-tokens";

    private string CallbackUrl => hunters.Value.Sso.CallbackUrl;

    /// <summary>The tokens from this request's code exchange, for the login events.</summary>
    public static EveTokenSet? GetTokens(HttpContext context) => context.Items[TokensItem] as EveTokenSet;

    protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri) =>
        base.BuildChallengeUrl(properties, CallbackUrl);

    protected override async Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context)
    {
        try
        {
            var tokens = await sso.ExchangeCodeAsync(context.Code, CallbackUrl, Context.RequestAborted);
            Context.Items[TokensItem] = tokens;

            var expiresIn = Math.Max(0, (int)(tokens.ExpiresAt - TimeProvider.GetUtcNow()).TotalSeconds);
            return OAuthTokenResponse.Success(JsonSerializer.SerializeToDocument(new Dictionary<string, object>
            {
                ["access_token"] = tokens.AccessToken,
                ["refresh_token"] = tokens.RefreshToken,
                ["token_type"] = "Bearer",
                ["expires_in"] = expiresIn,
            }));
        }
        catch (EveSsoException ex)
        {
            return OAuthTokenResponse.Failed(ex);
        }
    }
}

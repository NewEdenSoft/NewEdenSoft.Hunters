namespace NewEdenSoft.Hunters.Server.Infrastructure.Eve;

/// <summary>
/// EVE SSO token endpoint calls as a confidential client, authenticated with the application's
/// client secret. The browser half of the login (redirect, state, correlation, callback) is the
/// ASP.NET Core OAuth handler's job; see <see cref="EveOAuthHandler"/>.
/// </summary>
public interface IEveSsoClient
{
    /// <summary>Exchanges an authorization code. <paramref name="redirectUri"/> must match the one sent to EVE.</summary>
    Task<EveTokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken);

    /// <summary>Uses up <paramref name="refreshToken"/>; the result carries its replacement.</summary>
    Task<EveTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes a refresh token. Best effort: failures are logged, not thrown.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}

/// <summary>Tokens from EVE, with the identity read from the validated access token.</summary>
public sealed record EveTokenSet(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, EveIdentity Identity);

/// <summary>Who the token belongs to, from the validated access token.</summary>
public sealed record EveIdentity(long CharacterId, string Name, string OwnerHash, IReadOnlyList<string> Scopes);

/// <summary>An error response from EVE SSO, or a token that failed validation.</summary>
public sealed class EveSsoException(string error, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Error { get; } = error;

    /// <summary>The code or refresh token is invalid, expired or already used.</summary>
    public bool IsInvalidGrant => Error == "invalid_grant";
}

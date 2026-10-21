namespace NewEdenSoft.Hunters.Client.Sessions;

/// <summary>
/// EVE access tokens for the signed-in account's characters, for the client's own ESI calls. The
/// server holds the refresh tokens; see docs/authentication.md, "Access tokens for clients".
/// </summary>
public interface ICharacterTokenProvider
{
    /// <summary>
    /// A token valid for at least another minute, cached until then. When the server can no longer
    /// refresh the character's token, the character is marked <c>NeedsReauth</c> on the session's
    /// account and a <see cref="HuntersApiException"/> with code <c>needs_reauth</c> is thrown.
    /// </summary>
    ValueTask<string> GetAccessTokenAsync(long characterId, CancellationToken cancellationToken = default);

    /// <summary>Drops a cached token that ESI rejected, so the next request asks the server again.</summary>
    void InvalidateAccessToken(long characterId);
}

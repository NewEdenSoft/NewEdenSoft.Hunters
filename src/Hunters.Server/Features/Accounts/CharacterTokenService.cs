using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Server.Infrastructure.Persistence;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Features.Accounts;

public enum AccessTokenStatus
{
    Ok,
    NotFound,
    NeedsReauth,
    Unavailable,
}

public sealed record AccessTokenResult(AccessTokenStatus Status, string? AccessToken = null, DateTimeOffset ExpiresAt = default);

/// <summary>
/// Hands out EVE access tokens for client ESI calls. The server is the only party that refreshes:
/// EVE refresh tokens are consumed on use, so refreshes for a character run one at a time and the
/// replacement refresh token is saved before any access token is returned.
/// </summary>
public sealed partial class CharacterTokenService(
    HuntersDbContext db,
    IEveSsoClient sso,
    TokenProtector protector,
    AccountService accounts,
    TimeProvider time,
    ILogger<CharacterTokenService> logger)
{
    /// <summary>Access tokens with less than this left are refreshed rather than handed out.</summary>
    public static readonly TimeSpan MinimumLifetime = TimeSpan.FromSeconds(60);

    private static readonly ConcurrentDictionary<long, SemaphoreSlim> Locks = new();

    public async Task<AccessTokenResult> GetAccessTokenAsync(Guid accountId, long characterId, CancellationToken cancellationToken)
    {
        var cached = await TryCachedAsync(accountId, characterId, cancellationToken);
        if (cached is not null)
            return cached;

        var gate = Locks.GetOrAdd(characterId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Another request may have refreshed while we waited.
            db.ChangeTracker.Clear();
            cached = await TryCachedAsync(accountId, characterId, cancellationToken);
            return cached ?? await RefreshAsync(accountId, characterId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AccessTokenResult?> TryCachedAsync(Guid accountId, long characterId, CancellationToken cancellationToken)
    {
        var character = await db.Characters.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == characterId && c.AccountId == accountId, cancellationToken);

        if (character is null)
            return new AccessTokenResult(AccessTokenStatus.NotFound);
        if (character.Status == CharacterStatus.NeedsReauth || character.EncryptedRefreshToken is null)
            return new AccessTokenResult(AccessTokenStatus.NeedsReauth);

        if (character.AccessTokenExpiresAt is { } expiresAt
            && expiresAt - time.GetUtcNow() > MinimumLifetime
            && protector.Unprotect(character.EncryptedAccessToken) is { } accessToken)
        {
            return new AccessTokenResult(AccessTokenStatus.Ok, accessToken, expiresAt);
        }

        return null;
    }

    private async Task<AccessTokenResult> RefreshAsync(Guid accountId, long characterId, CancellationToken cancellationToken)
    {
        var character = await db.Characters.FirstAsync(c => c.Id == characterId && c.AccountId == accountId, cancellationToken);

        var refreshToken = protector.Unprotect(character.EncryptedRefreshToken);
        if (refreshToken is null)
            return await MarkNeedsReauthAsync(character, cancellationToken);

        EveTokenSet tokens;
        try
        {
            tokens = await sso.RefreshAsync(refreshToken, cancellationToken);
        }
        catch (EveSsoException ex) when (ex.IsInvalidGrant)
        {
            LogRefreshRejected(characterId, ex.Message);
            return await MarkNeedsReauthAsync(character, cancellationToken);
        }
        catch (EveSsoException ex)
        {
            LogRefreshFailed(ex, characterId);
            return new AccessTokenResult(AccessTokenStatus.Unavailable);
        }

        if (tokens.Identity.OwnerHash != character.OwnerHash)
        {
            // Transferred to another player: the token is theirs now, not this account's.
            await sso.RevokeAsync(tokens.RefreshToken, cancellationToken);
            await accounts.UnlinkTransferredAsync(character, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return new AccessTokenResult(AccessTokenStatus.NotFound);
        }

        Apply(character, tokens, protector, time.GetUtcNow());
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another server instance rotated the token first; use what it stored.
            db.ChangeTracker.Clear();
            return await TryCachedAsync(accountId, characterId, cancellationToken)
                ?? new AccessTokenResult(AccessTokenStatus.Unavailable);
        }

        return new AccessTokenResult(AccessTokenStatus.Ok, tokens.AccessToken, tokens.ExpiresAt);
    }

    /// <summary>Stores a fresh token set on a character (login or refresh).</summary>
    public static void Apply(Character character, EveTokenSet tokens, TokenProtector protector, DateTimeOffset now)
    {
        character.Name = tokens.Identity.Name;
        character.OwnerHash = tokens.Identity.OwnerHash;
        character.Scopes = [.. tokens.Identity.Scopes];
        character.EncryptedRefreshToken = protector.Protect(tokens.RefreshToken);
        character.EncryptedAccessToken = protector.Protect(tokens.AccessToken);
        character.AccessTokenExpiresAt = tokens.ExpiresAt;
        character.LastRefreshedAt = now;
        character.Status = CharacterStatus.Active;
    }

    private async Task<AccessTokenResult> MarkNeedsReauthAsync(Character character, CancellationToken cancellationToken)
    {
        character.Status = CharacterStatus.NeedsReauth;
        character.EncryptedRefreshToken = null;
        character.EncryptedAccessToken = null;
        character.AccessTokenExpiresAt = null;
        await db.SaveChangesAsync(cancellationToken);
        return new AccessTokenResult(AccessTokenStatus.NeedsReauth);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "EVE rejected the refresh token of character {CharacterId}: {Reason}")]
    private partial void LogRefreshRejected(long characterId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refreshing the token of character {CharacterId} failed")]
    private partial void LogRefreshFailed(Exception exception, long characterId);
}

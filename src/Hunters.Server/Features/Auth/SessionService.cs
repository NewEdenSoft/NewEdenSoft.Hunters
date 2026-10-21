using Microsoft.EntityFrameworkCore;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Server.Infrastructure.Persistence;
using NewEdenSoft.Hunters.Server.Infrastructure.Caching;

namespace NewEdenSoft.Hunters.Server.Features.Auth;

/// <summary>An authenticated app session: who is calling and through which session.</summary>
public sealed record SessionIdentity(Guid SessionId, Guid AccountId);

/// <summary>
/// Creates, validates and revokes app sessions. Desktop sessions carry an opaque bearer token
/// (only its hash is stored); web sessions are referenced from the auth cookie. Validity is cached
/// briefly; revoking clears the cache entry so it takes effect at once on this instance.
/// </summary>
public sealed class SessionService(HuntersDbContext db, ICacheStore cache, TimeProvider time)
{
    public const string TokenPrefix = "hs_";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(5);

    public async Task<(Session Session, string Token)> CreateDesktopAsync(Guid accountId, string? deviceName, CancellationToken cancellationToken)
    {
        var token = Secrets.NewToken(TokenPrefix);
        var session = NewSession(accountId, SessionKind.Desktop, deviceName);
        session.TokenHash = Secrets.Sha256(token);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return (session, token);
    }

    public async Task<Session> CreateWebAsync(Guid accountId, string? deviceName, CancellationToken cancellationToken)
    {
        var session = NewSession(accountId, SessionKind.Web, deviceName);
        db.Sessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<SessionIdentity?> ValidateBearerAsync(string token, CancellationToken cancellationToken)
    {
        if (!token.StartsWith(TokenPrefix, StringComparison.Ordinal))
            return null;

        var hash = Secrets.Sha256(token);
        var key = "session:token:" + Convert.ToBase64String(hash);
        var identity = await cache.GetAsync<SessionIdentity>(key, cancellationToken);
        if (identity is null)
        {
            var session = await db.Sessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TokenHash == hash && s.RevokedAt == null, cancellationToken);
            if (session is null)
                return null;

            identity = new SessionIdentity(session.Id, session.AccountId);
            await cache.SetAsync(key, identity, CacheDuration, cancellationToken);
        }

        return await IsActiveAsync(identity.SessionId, cancellationToken) ? identity : null;
    }

    /// <summary>True while the session exists and is not revoked. Also records activity.</summary>
    public async Task<bool> IsActiveAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (await cache.GetAsync<string>(ValidKey(sessionId), cancellationToken) is not null)
            return true;

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null || session.RevokedAt is not null)
            return false;

        var now = time.GetUtcNow();
        if (now - session.LastSeenAt > LastSeenResolution)
        {
            session.LastSeenAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }

        await cache.SetAsync(ValidKey(sessionId), "valid", CacheDuration, cancellationToken);
        return true;
    }

    public async Task<List<Session>> ListAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.Sessions.AsNoTracking()
            .Where(s => s.AccountId == accountId && s.RevokedAt == null)
            .OrderByDescending(s => s.LastSeenAt)
            .ToListAsync(cancellationToken);

    /// <summary>Revokes one session of an account. False if it does not exist or belongs to someone else.</summary>
    public async Task<bool> RevokeAsync(Guid accountId, Guid sessionId, CancellationToken cancellationToken)
    {
        var count = await RevokeWhereAsync(s => s.AccountId == accountId && s.Id == sessionId, cancellationToken);
        return count > 0;
    }

    public Task<int> RevokeAllAsync(Guid accountId, CancellationToken cancellationToken) =>
        RevokeWhereAsync(s => s.AccountId == accountId, cancellationToken);

    private async Task<int> RevokeWhereAsync(System.Linq.Expressions.Expression<Func<Session, bool>> predicate, CancellationToken cancellationToken)
    {
        var sessions = await db.Sessions.Where(predicate).Where(s => s.RevokedAt == null).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            await cache.RemoveAsync(ValidKey(session.Id), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return sessions.Count;
    }

    private Session NewSession(Guid accountId, SessionKind kind, string? deviceName)
    {
        var now = time.GetUtcNow();
        return new Session
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            Kind = kind,
            DeviceName = deviceName is { Length: > 0 } name ? name[..Math.Min(name.Length, 128)] : null,
            CreatedAt = now,
            LastSeenAt = now,
        };
    }

    private static string ValidKey(Guid sessionId) => $"session:valid:{sessionId}";
}

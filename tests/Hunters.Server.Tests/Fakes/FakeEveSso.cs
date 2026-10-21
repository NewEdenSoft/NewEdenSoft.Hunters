using System.Collections.Concurrent;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Tests.Fakes;

/// <summary>
/// In-memory EVE SSO. Codes are issued per character; refresh tokens rotate and are consumed on
/// use, as with the real SSO.
/// </summary>
public sealed class FakeEveSso : IEveSsoClient
{
    public static readonly string[] AllScopes =
    [
        "esi-location.read_location.v1",
        "esi-location.read_online.v1",
        "esi-location.read_ship_type.v1",
        "esi-ui.write_waypoint.v1",
    ];

    private readonly ConcurrentDictionary<string, EveIdentity> _codes = new();
    private readonly ConcurrentDictionary<string, EveIdentity> _refreshTokens = new();

    /// <summary>Lifetime of issued access tokens. Short values force a refresh on the next request.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(20);

    /// <summary>Owner hash returned on the next refresh of a character, simulating a transfer.</summary>
    public ConcurrentDictionary<long, string> OwnerOverrides { get; } = new();

    public ConcurrentBag<string> Revoked { get; } = [];

    public int RefreshCount;

    public static EveIdentity Character(long id, string name, string? owner = null, string[]? scopes = null) =>
        new(id, name, owner ?? $"owner-{id}", scopes ?? AllScopes);

    /// <summary>Simulates the user logging in at EVE: returns the code EVE would send to the callback.</summary>
    public string IssueCode(EveIdentity identity)
    {
        var code = "code-" + Guid.NewGuid().ToString("N");
        _codes[code] = identity;
        return code;
    }

    public bool IsRefreshTokenValid(string token) => _refreshTokens.ContainsKey(token);

    /// <summary>The redirect URI sent with the last code exchange.</summary>
    public string? LastRedirectUri { get; private set; }

    public Task<EveTokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken)
    {
        LastRedirectUri = redirectUri;
        return Exchange(code);
    }

    private Task<EveTokenSet> Exchange(string code) =>
        _codes.TryRemove(code, out var identity)
            ? Task.FromResult(Issue(identity))
            : throw new EveSsoException("invalid_grant", "Unknown or used code.");

    public Task<EveTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (!_refreshTokens.TryRemove(refreshToken, out var identity))
            throw new EveSsoException("invalid_grant", "Unknown or used refresh token.");

        Interlocked.Increment(ref RefreshCount);
        if (OwnerOverrides.TryRemove(identity.CharacterId, out var owner))
            identity = identity with { OwnerHash = owner };

        return Task.FromResult(Issue(identity));
    }

    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        _refreshTokens.TryRemove(refreshToken, out _);
        Revoked.Add(refreshToken);
        return Task.CompletedTask;
    }

    private EveTokenSet Issue(EveIdentity identity)
    {
        var refresh = "rt-" + Guid.NewGuid().ToString("N");
        _refreshTokens[refresh] = identity;
        return new EveTokenSet("at-" + Guid.NewGuid().ToString("N"), refresh, DateTimeOffset.UtcNow + AccessTokenLifetime, identity);
    }
}

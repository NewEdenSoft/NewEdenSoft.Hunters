using System.ComponentModel.DataAnnotations;
using NewEdenSoft.Hunters.Contracts.Account;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Persistence;

public sealed class Account
{
    public Guid Id { get; set; }

    public long? MainCharacterId { get; set; }

    public bool IsDisabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<Character> Characters { get; set; } = [];

    public List<Session> Sessions { get; set; } = [];
}

/// <summary>An EVE character linked to exactly one account. The key is the EVE character id.</summary>
public sealed class Character
{
    public long Id { get; set; }

    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    [MaxLength(64)]
    public string Name { get; set; } = "";

    /// <summary>EVE SSO owner hash; changes when the character is transferred to another player.</summary>
    [MaxLength(64)]
    public string OwnerHash { get; set; } = "";

    public string[] Scopes { get; set; } = [];

    public CharacterStatus Status { get; set; }

    /// <summary>Data Protection encrypted. Null once lost or revoked.</summary>
    public string? EncryptedRefreshToken { get; set; }

    /// <summary>Data Protection encrypted cache of the last access token.</summary>
    public string? EncryptedAccessToken { get; set; }

    public DateTimeOffset? AccessTokenExpiresAt { get; set; }

    public DateTimeOffset AddedAt { get; set; }

    public DateTimeOffset? LastRefreshedAt { get; set; }

    /// <summary>Optimistic concurrency token (PostgreSQL <c>xmin</c>) guarding token rotation.</summary>
    [Timestamp]
    public uint Version { get; set; }
}

public sealed class Session
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Account Account { get; set; } = null!;

    public SessionKind Kind { get; set; }

    [MaxLength(128)]
    public string? DeviceName { get; set; }

    /// <summary>SHA-256 of the desktop bearer token. Null for web sessions (cookie-based).</summary>
    public byte[]? TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class AccessKey
{
    public Guid Id { get; set; }

    [MaxLength(128)]
    public string Label { get; set; } = "";

    public byte[] KeyHash { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public int? MaxUses { get; set; }

    public int Uses { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsUsable(DateTimeOffset now) =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > now) && (MaxUses is null || Uses < MaxUses);
}

/// <summary>
/// ESI's ship, pod and NPC kills per solar system in the hour ending at <see cref="PeriodEnd"/> (its
/// <c>Last-Modified</c>). One row per hour, the systems as parallel arrays: about 2,800 systems an
/// hour would otherwise be 2,800 rows. Only systems with kills are listed.
/// </summary>
public sealed class SystemKillsSnapshot
{
    public DateTimeOffset PeriodEnd { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public int[] SystemIds { get; set; } = [];

    public int[] ShipKills { get; set; } = [];

    public int[] PodKills { get; set; } = [];

    public int[] NpcKills { get; set; } = [];
}

/// <summary>ESI's ship jumps per solar system in the hour ending at <see cref="PeriodEnd"/>, as parallel arrays.</summary>
public sealed class SystemJumpsSnapshot
{
    public DateTimeOffset PeriodEnd { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public int[] SystemIds { get; set; } = [];

    public int[] ShipJumps { get; set; } = [];
}

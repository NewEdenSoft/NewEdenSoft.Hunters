namespace NewEdenSoft.Hunters.Contracts.Admin;

public sealed record AccessKeyInfo(
    Guid Id,
    string Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    int? MaxUses,
    int Uses,
    bool IsRevoked);

public sealed record CreateAccessKeyRequest(string Label, DateTimeOffset? ExpiresAt = null, int? MaxUses = null);

/// <param name="Key">The key itself. Shown once; only its hash is stored.</param>
public sealed record CreatedAccessKey(AccessKeyInfo AccessKey, string Key);

public sealed record AdminAccountInfo(
    Guid Id,
    long? MainCharacterId,
    string? MainCharacterName,
    int CharacterCount,
    int ActiveSessionCount,
    bool IsDisabled,
    DateTimeOffset CreatedAt);

/// <param name="Profiles">The hunting profile ids to rebuild; null or empty for all of them.</param>
public sealed record RebuildHuntingRequest(IReadOnlyList<string>? Profiles = null);

/// <summary>A request to rebuild hunting profiles from the stored kills, which the intel service carries out.</summary>
/// <param name="Profiles">The profile ids; empty for all of them.</param>
/// <param name="StartedAt">When the intel service took it up; null while waiting (it looks every 30 seconds).</param>
/// <param name="CompletedAt">When the rebuild finished; null while waiting or running.</param>
/// <param name="Error">Why the last attempt failed; the intel service tries again.</param>
public sealed record HuntingRebuildInfo(
    long Id,
    IReadOnlyList<string> Profiles,
    DateTimeOffset RequestedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string? Error);

public static class AdminPaths
{
    public const string AccessKeys = "/api/admin/access-keys";
    public const string Accounts = "/api/admin/accounts";

    /// <summary><c>POST</c> asks for a rebuild; <c>GET</c> lists the latest requests.</summary>
    public const string HuntingRebuild = "/api/admin/hunting/rebuild";

    public static string AccessKey(Guid id) => $"/api/admin/access-keys/{id}";
    public static string AccountDisable(Guid id) => $"/api/admin/accounts/{id}/disable";
    public static string AccountEnable(Guid id) => $"/api/admin/accounts/{id}/enable";
    public static string AccountSessions(Guid id) => $"/api/admin/accounts/{id}/sessions";
}

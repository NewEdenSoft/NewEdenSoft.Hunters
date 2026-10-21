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

public static class AdminPaths
{
    public const string AccessKeys = "/api/admin/access-keys";
    public const string Accounts = "/api/admin/accounts";

    public static string AccessKey(Guid id) => $"/api/admin/access-keys/{id}";
    public static string AccountDisable(Guid id) => $"/api/admin/accounts/{id}/disable";
    public static string AccountEnable(Guid id) => $"/api/admin/accounts/{id}/enable";
    public static string AccountSessions(Guid id) => $"/api/admin/accounts/{id}/sessions";
}

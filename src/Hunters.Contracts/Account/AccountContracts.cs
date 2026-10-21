using System.Text.Json.Serialization;

namespace NewEdenSoft.Hunters.Contracts.Account;

public sealed record AccountInfo(Guid Id, long? MainCharacterId, bool IsAdmin, IReadOnlyList<CharacterInfo> Characters);

public sealed record CharacterInfo(
    long Id,
    string Name,
    bool IsMain,
    CharacterStatus Status,
    IReadOnlyList<string> MissingScopes,
    DateTimeOffset AddedAt);

[JsonConverter(typeof(JsonStringEnumConverter<CharacterStatus>))]
public enum CharacterStatus
{
    /// <summary>Token valid and all required scopes granted.</summary>
    Active,

    /// <summary>Refresh failed or the token was lost; log in with the character again.</summary>
    NeedsReauth,

    /// <summary>The server now requires scopes this character has not granted; log in with it again.</summary>
    MissingScopes,
}

public sealed record AccessTokenResponse(long CharacterId, string AccessToken, DateTimeOffset ExpiresAt);

public sealed record SetMainCharacterRequest(long CharacterId);

public sealed record SessionInfo(Guid Id, SessionKind Kind, string? DeviceName, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, bool IsCurrent);

[JsonConverter(typeof(JsonStringEnumConverter<SessionKind>))]
public enum SessionKind
{
    Web,
    Desktop,
}

public static class AccountPaths
{
    public const string Account = "/api/account";
    public const string MainCharacter = "/api/account/main";
    public const string Characters = "/api/characters";
    public const string Sessions = "/api/sessions";

    public static string Character(long id) => $"/api/characters/{id}";
    public static string AccessToken(long id) => $"/api/characters/{id}/access-token";
    public static string Session(Guid id) => $"/api/sessions/{id}";
}

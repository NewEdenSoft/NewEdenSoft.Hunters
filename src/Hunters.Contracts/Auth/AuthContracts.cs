using NewEdenSoft.Hunters.Contracts.Account;

namespace NewEdenSoft.Hunters.Contracts.Auth;

/// <summary>
/// Starts a desktop login. The app listens on <c>http://127.0.0.1:{Port}/</c> and sends the
/// SHA-256 of a random verifier (base64url, no padding) as <see cref="AppChallenge"/>.
/// Send with the app session's bearer token to add a character to the signed-in account.
/// </summary>
public sealed record BeginDesktopLoginRequest(int Port, string AppChallenge, string? AccessKey = null, string? DeviceName = null);

/// <param name="LoginId">Identifies this login; appears in the browser URL.</param>
/// <param name="BrowserUrl">Absolute URL to open in the user's browser.</param>
public sealed record BeginDesktopLoginResponse(string LoginId, string BrowserUrl, DateTimeOffset ExpiresAt);

/// <summary>Exchanges the single-use code delivered to the loopback listener for an app session.</summary>
public sealed record RedeemDesktopLoginRequest(string LoginId, string LoginCode, string AppVerifier);

/// <param name="SessionToken">Bearer token for this device. The desktop app keeps it in memory only (see docs/decisions.md, D20).</param>
public sealed record DesktopLoginResult(string SessionToken, Guid SessionId, bool IsNewAccount, long CharacterId, AccountInfo Account);

public static class AuthPaths
{
    public const string DesktopBegin = "/auth/desktop/begin";
    public const string DesktopRedeem = "/auth/desktop/redeem";
    public const string DesktopBrowser = "/auth/desktop/";
    public const string WebLogin = "/auth/web/login";
    public const string WebLogout = "/auth/web/logout";
    public const string EveCallback = "/auth/eve/callback";

    /// <summary>Path on the desktop's loopback listener the server redirects to.</summary>
    public const string LoopbackCallback = "/callback";
}

/// <summary>Machine-readable <c>code</c> on problem responses and loopback/web login errors.</summary>
public static class AuthErrorCodes
{
    public const string LoginExpired = "login_expired";
    public const string LoginCancelled = "login_cancelled";
    public const string SsoFailed = "sso_failed";
    public const string AccessKeyRequired = "access_key_required";
    public const string AccessKeyInvalid = "access_key_invalid";
    public const string AdmissionDenied = "admission_denied";
    public const string AccountDisabled = "account_disabled";
    public const string CharacterLinkedElsewhere = "character_linked_elsewhere";
    public const string InvalidLoginCode = "invalid_login_code";
    public const string NeedsReauth = "needs_reauth";
}

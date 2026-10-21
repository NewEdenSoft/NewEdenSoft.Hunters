using Microsoft.EntityFrameworkCore;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Features.Accounts;
using NewEdenSoft.Hunters.Server.Features.Admission;
using NewEdenSoft.Hunters.Server.Infrastructure.Persistence;
using NewEdenSoft.Hunters.Server.Infrastructure.Caching;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;
using AccountEntity = NewEdenSoft.Hunters.Server.Infrastructure.Persistence.Account;

namespace NewEdenSoft.Hunters.Server.Features.Auth;

/// <summary>A finished desktop login waiting to be redeemed with its single-use code.</summary>
public sealed record CompletedDesktopLogin(string LoginId, string AppChallenge, Guid AccountId, long CharacterId, bool IsNewAccount, string? DeviceName);

public sealed record LoginError(string Code, string Message);

public sealed record LoginOutcome(Guid AccountId, long CharacterId, bool IsNewAccount, LoginError? Error)
{
    public bool Succeeded => Error is null;

    public static LoginOutcome Failed(string code, string message) => new(Guid.Empty, 0, false, new LoginError(code, message));
}

/// <summary>
/// Login logic around the standard OAuth handler: preparing desktop logins, deciding what a
/// verified EVE login means for accounts (sign in, link, create, admission), and the desktop
/// handoff with a single-use code redeemed by the app's verifier. See docs/authentication.md.
/// </summary>
public sealed class LoginService(
    HuntersDbContext db,
    ICacheStore cache,
    IEveSsoClient sso,
    TokenProtector protector,
    AdmissionService admission,
    AccountService accounts,
    SessionService sessions,
    TimeProvider time)
{
    public static readonly TimeSpan LoginLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan LoginCodeLifetime = TimeSpan.FromSeconds(60);

    /// <summary>Registers a desktop login. The app then opens the returned browser URL.</summary>
    public async Task<(BeginDesktopLoginResponse? Response, LoginError? Error)> BeginDesktopAsync(
        BeginDesktopLoginRequest request, Guid? currentAccountId, string serverBaseUrl, CancellationToken cancellationToken)
    {
        if (request.Port is < 1024 or > 65535 || request.AppChallenge is not { Length: 43 })
            return (null, new LoginError("invalid_request", "The login request is malformed."));

        var (accessKeyId, error) = await ResolveAccessKeyAsync(request.AccessKey, cancellationToken);
        if (error is not null)
            return (null, error);

        var login = new LoginRequest(
            LoginKind.Desktop, currentAccountId, accessKeyId, request.DeviceName,
            LoginId: Secrets.NewToken(), LoopbackPort: request.Port, AppChallenge: request.AppChallenge);
        await cache.SetAsync(LoginIdKey(login.LoginId!), login, LoginLifetime, cancellationToken);

        var browserUrl = serverBaseUrl.TrimEnd('/') + AuthPaths.DesktopBrowser + login.LoginId;
        return (new BeginDesktopLoginResponse(login.LoginId!, browserUrl, time.GetUtcNow() + LoginLifetime), null);
    }

    /// <summary>Takes a pending desktop login when its browser link is opened. Single use.</summary>
    public ValueTask<LoginRequest?> TakeDesktopLoginAsync(string loginId, CancellationToken cancellationToken) =>
        cache.TakeAsync<LoginRequest>(LoginIdKey(loginId), cancellationToken);

    public async Task<(LoginRequest? Request, LoginError? Error)> PrepareWebAsync(
        string? accessKey, string? returnUrl, Guid? currentAccountId, string? deviceName, CancellationToken cancellationToken)
    {
        var (accessKeyId, error) = await ResolveAccessKeyAsync(accessKey, cancellationToken);
        return error is not null
            ? (null, error)
            : (new LoginRequest(LoginKind.Web, currentAccountId, accessKeyId, deviceName, ReturnUrl: returnUrl), null);
    }

    /// <summary>
    /// Applies a verified EVE login: signs the character in, links it to the signed-in account, or
    /// creates an account, subject to admission. A refused login revokes the new token.
    /// </summary>
    public async Task<LoginOutcome> CompleteAsync(LoginRequest login, EveTokenSet tokens, CancellationToken cancellationToken)
    {
        var outcome = await LinkAsync(login, tokens, cancellationToken);
        if (!outcome.Succeeded)
            await sso.RevokeAsync(tokens.RefreshToken, cancellationToken);
        return outcome;
    }

    /// <summary>Issues the single-use code for a successful desktop login and returns the loopback redirect URL.</summary>
    public async Task<string> CreateDesktopRedirectAsync(LoginRequest login, LoginOutcome outcome, CancellationToken cancellationToken)
    {
        var code = Secrets.NewToken();
        var completed = new CompletedDesktopLogin(login.LoginId!, login.AppChallenge!, outcome.AccountId, outcome.CharacterId, outcome.IsNewAccount, login.DeviceName);
        await cache.SetAsync(CodeKey(code), completed, LoginCodeLifetime, cancellationToken);
        return LoopbackUrl(login, new() { ["loginId"] = login.LoginId, ["code"] = code });
    }

    public static string DesktopErrorRedirect(LoginRequest login, LoginError error) =>
        LoopbackUrl(login, new() { ["loginId"] = login.LoginId, ["error"] = error.Code, ["error_description"] = error.Message });

    public async Task<(DesktopLoginResult? Result, LoginError? Error)> RedeemDesktopAsync(RedeemDesktopLoginRequest request, CancellationToken cancellationToken)
    {
        var invalid = new LoginError(AuthErrorCodes.InvalidLoginCode, "The login code is invalid or has expired. Log in again.");

        var completed = await cache.TakeAsync<CompletedDesktopLogin>(CodeKey(request.LoginCode), cancellationToken);
        if (completed is null
            || completed.LoginId != request.LoginId
            || !Secrets.VerifyChallenge(request.AppVerifier, completed.AppChallenge))
        {
            return (null, invalid);
        }

        var (session, token) = await sessions.CreateDesktopAsync(completed.AccountId, completed.DeviceName, cancellationToken);
        var account = await accounts.GetAsync(completed.AccountId, cancellationToken);
        return account is null
            ? (null, invalid)
            : (new DesktopLoginResult(token, session.Id, completed.IsNewAccount, completed.CharacterId, account), null);
    }

    private async Task<LoginOutcome> LinkAsync(LoginRequest login, EveTokenSet tokens, CancellationToken cancellationToken)
    {
        var identity = tokens.Identity;
        var now = time.GetUtcNow();

        var existing = await db.Characters.Include(c => c.Account).FirstOrDefaultAsync(c => c.Id == identity.CharacterId, cancellationToken);
        if (existing is not null && existing.OwnerHash != identity.OwnerHash)
        {
            await accounts.UnlinkTransferredAsync(existing, cancellationToken);
            existing = null;
        }

        AccountEntity account;
        var isNewAccount = false;

        if (login.AccountId is { } targetId)
        {
            // Adding a character to the signed-in account.
            var target = await db.Accounts.FirstOrDefaultAsync(a => a.Id == targetId, cancellationToken);
            if (target is null)
                return LoginOutcome.Failed(AuthErrorCodes.LoginExpired, "Your session has ended. Log in again.");
            account = target;

            if (account.IsDisabled)
                return LoginOutcome.Failed(AuthErrorCodes.AccountDisabled, "This account has been disabled.");
            if (existing is not null && existing.AccountId != account.Id)
                return LoginOutcome.Failed(AuthErrorCodes.CharacterLinkedElsewhere, $"{identity.Name} is already linked to another account.");

            var check = await admission.CheckAsync(admission.SubjectFor(account.MainCharacterId, identity.CharacterId), identity.CharacterId, cancellationToken);
            if (!check.Allowed)
                return LoginOutcome.Failed(AuthErrorCodes.AdmissionDenied, check.Reason!);
        }
        else if (existing is not null)
        {
            // Signing in with a known character.
            account = existing.Account;
            if (account.IsDisabled)
                return LoginOutcome.Failed(AuthErrorCodes.AccountDisabled, "This account has been disabled.");

            var check = await admission.CheckAsync(admission.SubjectFor(account.MainCharacterId, identity.CharacterId), identity.CharacterId, cancellationToken);
            if (!check.Allowed)
                return LoginOutcome.Failed(AuthErrorCodes.AdmissionDenied, check.Reason!);
        }
        else
        {
            // First login with this character: a new account.
            AccessKey? accessKey = null;
            if (admission.AccessKeyRequiredForNewAccounts)
            {
                accessKey = login.AccessKeyId is { } keyId
                    ? await db.AccessKeys.FirstOrDefaultAsync(k => k.Id == keyId, cancellationToken)
                    : null;
                if (accessKey is null || !accessKey.IsUsable(now))
                    return LoginOutcome.Failed(AuthErrorCodes.AccessKeyRequired, "This server needs an access key to create an account.");
            }

            var check = await admission.CheckAsync(identity.CharacterId, identity.CharacterId, cancellationToken);
            if (!check.Allowed)
                return LoginOutcome.Failed(AuthErrorCodes.AdmissionDenied, check.Reason!);

            account = new AccountEntity { Id = Guid.CreateVersion7(), CreatedAt = now };
            db.Accounts.Add(account);
            if (accessKey is not null)
                accessKey.Uses++;
            isNewAccount = true;
        }

        var character = existing;
        if (character is null)
        {
            character = new Character { Id = identity.CharacterId, AccountId = account.Id, AddedAt = now };
            db.Characters.Add(character);
        }

        CharacterTokenService.Apply(character, tokens, protector, now);
        account.MainCharacterId ??= character.Id;

        await db.SaveChangesAsync(cancellationToken);
        return new LoginOutcome(account.Id, character.Id, isNewAccount, null);
    }

    private async Task<(Guid? AccessKeyId, LoginError? Error)> ResolveAccessKeyAsync(string? accessKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessKey))
            return (null, null);

        var key = await admission.FindUsableAccessKeyAsync(accessKey, cancellationToken);
        return key is null
            ? (null, new LoginError(AuthErrorCodes.AccessKeyInvalid, "That access key is not valid."))
            : (key.Id, null);
    }

    private static string LoopbackUrl(LoginRequest login, Dictionary<string, string?> query) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
            $"http://127.0.0.1:{login.LoopbackPort}{AuthPaths.LoopbackCallback}", query);

    private static string LoginIdKey(string loginId) => $"login:desktop:{loginId}";
    private static string CodeKey(string code) => $"login:code:{Secrets.Sha256Base64Url(code)}";
}

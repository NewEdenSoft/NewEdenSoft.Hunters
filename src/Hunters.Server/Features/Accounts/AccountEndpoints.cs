using System.Security.Claims;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Features.Auth;

namespace NewEdenSoft.Hunters.Server.Features.Accounts;

/// <summary>The signed-in user's account, characters, access tokens and sessions.</summary>
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccounts(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("").RequireAuthorization().WithTags("Account");

        api.MapGet(AccountPaths.Account, async (ClaimsPrincipal user, AccountService accounts, CancellationToken ct) =>
            await accounts.GetAsync(Session(user).AccountId, ct) is { } account ? Results.Ok(account) : Results.NotFound());

        api.MapDelete(AccountPaths.Account, async (ClaimsPrincipal user, AccountService accounts, CancellationToken ct) =>
            await accounts.DeleteAccountAsync(Session(user).AccountId, ct) ? Results.NoContent() : Results.NotFound());

        api.MapPut(AccountPaths.MainCharacter, async (SetMainCharacterRequest request, ClaimsPrincipal user, AccountService accounts, CancellationToken ct) =>
            ToResult(await accounts.SetMainAsync(Session(user).AccountId, request.CharacterId, ct)));

        api.MapDelete(AccountPaths.Characters + "/{characterId:long}", async (long characterId, ClaimsPrincipal user, AccountService accounts, CancellationToken ct) =>
            ToResult(await accounts.RemoveCharacterAsync(Session(user).AccountId, characterId, ct)));

        api.MapPost(AccountPaths.Characters + "/{characterId:long}/access-token", async (long characterId, ClaimsPrincipal user, CharacterTokenService tokens, CancellationToken ct) =>
        {
            var result = await tokens.GetAccessTokenAsync(Session(user).AccountId, characterId, ct);
            return result.Status switch
            {
                AccessTokenStatus.Ok => Results.Ok(new AccessTokenResponse(characterId, result.AccessToken!, result.ExpiresAt)),
                AccessTokenStatus.NotFound => Results.NotFound(),
                AccessTokenStatus.NeedsReauth => Results.Problem(
                    title: "Log in with this character again.",
                    statusCode: StatusCodes.Status409Conflict,
                    extensions: new Dictionary<string, object?> { ["code"] = AuthErrorCodes.NeedsReauth }),
                _ => Results.Problem(title: "EVE Online could not be reached. Try again shortly.", statusCode: StatusCodes.Status503ServiceUnavailable),
            };
        });

        api.MapGet(AccountPaths.Sessions, async (ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
        {
            var current = Session(user);
            var list = await sessions.ListAsync(current.AccountId, ct);
            return list.Select(s => new SessionInfo(s.Id, s.Kind, s.DeviceName, s.CreatedAt, s.LastSeenAt, s.Id == current.SessionId)).ToList();
        });

        api.MapDelete(AccountPaths.Sessions + "/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal user, SessionService sessions, CancellationToken ct) =>
            await sessions.RevokeAsync(Session(user).AccountId, sessionId, ct) ? Results.NoContent() : Results.NotFound());

        return endpoints;
    }

    private static SessionIdentity Session(ClaimsPrincipal user) =>
        user.GetSession() ?? throw new InvalidOperationException("Endpoint requires an authenticated session.");

    private static IResult ToResult((AccountChangeResult Result, string? Reason) change) => change.Result switch
    {
        AccountChangeResult.Done => Results.NoContent(),
        AccountChangeResult.NotFound => Results.NotFound(),
        _ => Results.Problem(title: change.Reason, statusCode: StatusCodes.Status409Conflict),
    };
}

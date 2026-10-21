using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Client.Auth;
using NewEdenSoft.Hunters.Client.Sessions;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;
using NewEdenSoft.Hunters.Server.Infrastructure.Persistence;
using NewEdenSoft.Hunters.Server.Tests.Features.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Accounts;

/// <summary>The desktop's <see cref="HuntersSession"/>: character management and access tokens against the in-memory server.</summary>
[Collection(PostgresCollection.Name)]
public sealed class DesktopSessionTests(PostgresFixture postgres) : IDisposable
{
    private static readonly Uri Server = new("http://localhost/");

    private readonly HuntersServerFactory _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Adding_a_character_switches_to_the_new_session_and_ends_the_old_one()
    {
        var (session, browser, logins, token) = await LogInAsync("Main");
        var altId = TestIds.Character();
        browser.Identity = FakeEveSso.Character(altId, "Alt");

        var result = await session.AddCharacterAsync(logins);

        Assert.Equal(altId, result.CharacterId);
        Assert.Equal(["Alt", "Main"], session.Account.Characters.Select(c => c.Name).Order());
        Assert.Equal(result.SessionId, session.SessionId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(token).GetAsync(AccountPaths.Account)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await WithToken(result.SessionToken).GetAsync(AccountPaths.Account)).StatusCode);
        await session.RefreshAsync();
    }

    [Fact]
    public async Task Failed_token_refresh_marks_the_character_and_logging_in_again_fixes_it()
    {
        _factory.Sso.AccessTokenLifetime = TimeSpan.FromSeconds(30); // always refreshed on request
        var (session, browser, logins, token) = await LogInAsync("Expired");
        var characterId = session.Account.Characters.Single().Id;
        await _factory.Sso.RevokeAsync(await StoredRefreshTokenAsync(characterId), CancellationToken.None);
        var changes = 0;
        session.AccountChanged += (_, _) => changes++;

        var error = await Assert.ThrowsAsync<HuntersApiException>(() => session.GetAccessTokenAsync(characterId).AsTask());

        Assert.Equal(AuthErrorCodes.NeedsReauth, error.Code);
        Assert.Equal(CharacterStatus.NeedsReauth, session.Account.Characters.Single().Status);
        Assert.Equal(1, changes);

        await session.AddCharacterAsync(logins); // same character at EVE

        Assert.Equal(CharacterStatus.Active, session.Account.Characters.Single().Status);
        Assert.NotEmpty(await session.GetAccessTokenAsync(characterId));
    }

    [Fact]
    public async Task Access_tokens_are_cached_until_shortly_before_expiry()
    {
        var (session, _, _, token) = await LogInAsync("Cached");
        var characterId = session.Account.Characters.Single().Id;

        var first = await session.GetAccessTokenAsync(characterId);
        var second = await session.GetAccessTokenAsync(characterId);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Main_character_can_be_removed_after_choosing_another_main()
    {
        var (session, browser, logins, token) = await LogInAsync("First");
        var firstId = session.Account.Characters.Single().Id;
        browser.Identity = FakeEveSso.Character(TestIds.Character(), "Second");
        var secondId = (await session.AddCharacterAsync(logins)).CharacterId;

        var refused = await Assert.ThrowsAsync<HuntersApiException>(() => session.RemoveCharacterAsync(firstId));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        await session.SetMainCharacterAsync(secondId);
        await session.RemoveCharacterAsync(firstId);

        var remaining = Assert.Single(session.Account.Characters);
        Assert.Equal(secondId, remaining.Id);
        Assert.True(remaining.IsMain);
        Assert.NotEmpty(_factory.Sso.Revoked);
    }

    [Fact]
    public async Task Deleting_the_account_ends_the_session_and_removes_everything()
    {
        var (session, _, _, token) = await LogInAsync("Gone");
        var characterId = session.Account.Characters.Single().Id;
        var ended = false;
        session.Ended += (_, _) => ended = true;

        await session.DeleteAccountAsync();

        Assert.True(ended);
        Assert.True(session.IsEnded);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(token).GetAsync(AccountPaths.Account)).StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<HuntersDbContext>().Characters.AnyAsync(c => c.Id == characterId));
    }

    [Fact]
    public async Task Logging_out_revokes_the_session()
    {
        var (session, _, _, token) = await LogInAsync("Leaving");

        await session.LogOutAsync();

        Assert.True(session.IsEnded);
        Assert.Equal(HttpStatusCode.Unauthorized, (await WithToken(token).GetAsync(AccountPaths.Account)).StatusCode);
        var error = await Assert.ThrowsAsync<HuntersApiException>(() => session.RefreshAsync());
        Assert.Equal(HuntersApiException.SessionEnded, error.Code);
    }

    [Fact]
    public async Task Session_rejected_by_the_server_ends()
    {
        var (session, _, _, token) = await LogInAsync("Revoked elsewhere");
        // Revoked from another device, e.g. the web app's session list.
        Assert.Equal(HttpStatusCode.NoContent, (await WithToken(token).DeleteAsync(AccountPaths.Session(session.SessionId))).StatusCode);

        var error = await Assert.ThrowsAsync<HuntersApiException>(() => session.RefreshAsync());

        Assert.Equal(HuntersApiException.SessionEnded, error.Code);
        Assert.True(session.IsEnded);
    }

    /// <summary>Logs in a new character and returns its session, the browser for further logins and the session token.</summary>
    private async Task<(HuntersSession Session, FakeBrowser Browser, DesktopLoginClient Logins, string Token)> LogInAsync(string name)
    {
        var browser = new FakeBrowser(_factory, FakeEveSso.Character(TestIds.Character(), name));
        var logins = new DesktopLoginClient(_factory.CreateClient(), browser);
        var result = await logins.LoginAsync(Server, deviceName: "Test PC");
        return (new HuntersSession(_factory.CreateClient(), Server, result), browser, logins, result.SessionToken);
    }

    private HttpClient WithToken(string token) => new LoginDriver(_factory).AuthorizedClient(token);

    private async Task<string> StoredRefreshTokenAsync(long characterId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HuntersDbContext>();
        var character = await db.Characters.AsNoTracking().SingleAsync(c => c.Id == characterId);
        return scope.ServiceProvider.GetRequiredService<TokenProtector>().Unprotect(character.EncryptedRefreshToken)!;
    }
}

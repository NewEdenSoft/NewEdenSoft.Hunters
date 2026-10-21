using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Server.Infrastructure.Persistence;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;
using NewEdenSoft.Hunters.Server.Tests.Features.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class AccessTokenTests(PostgresFixture postgres) : IDisposable
{
    private readonly HuntersServerFactory _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Valid_access_token_is_returned_without_refreshing()
    {
        var driver = new LoginDriver(_factory);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Fresh"));

        var token = await driver.AuthorizedClient(login.SessionToken)
            .PostAsync(AccountPaths.AccessToken(login.CharacterId), null);

        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        Assert.Equal(0, _factory.Sso.RefreshCount);
    }

    [Fact]
    public async Task Expiring_token_is_refreshed_and_the_new_refresh_token_saved()
    {
        _factory.Sso.AccessTokenLifetime = TimeSpan.FromSeconds(30);
        var driver = new LoginDriver(_factory);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Stale"));
        var client = driver.AuthorizedClient(login.SessionToken);

        var first = await (await client.PostAsync(AccountPaths.AccessToken(login.CharacterId), null)).Content.ReadFromJsonAsync<AccessTokenResponse>();
        var second = await (await client.PostAsync(AccountPaths.AccessToken(login.CharacterId), null)).Content.ReadFromJsonAsync<AccessTokenResponse>();

        Assert.Equal(2, _factory.Sso.RefreshCount);
        Assert.NotEqual(first!.AccessToken, second!.AccessToken);
        Assert.True(_factory.Sso.IsRefreshTokenValid(await StoredRefreshTokenAsync(login.CharacterId)));
    }

    [Fact]
    public async Task Concurrent_requests_refresh_once()
    {
        _factory.Sso.AccessTokenLifetime = TimeSpan.FromSeconds(30);
        var driver = new LoginDriver(_factory);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Busy"));
        _factory.Sso.AccessTokenLifetime = TimeSpan.FromMinutes(20);
        var client = driver.AuthorizedClient(login.SessionToken);

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.PostAsync(AccountPaths.AccessToken(login.CharacterId), null)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, _factory.Sso.RefreshCount);
    }

    [Fact]
    public async Task Rejected_refresh_marks_the_character_for_reauth()
    {
        _factory.Sso.AccessTokenLifetime = TimeSpan.FromSeconds(30);
        var driver = new LoginDriver(_factory);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Revoked"));
        await _factory.Sso.RevokeAsync(await StoredRefreshTokenAsync(login.CharacterId), CancellationToken.None);
        var client = driver.AuthorizedClient(login.SessionToken);

        var response = await client.PostAsync(AccountPaths.AccessToken(login.CharacterId), null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var account = await client.GetFromJsonAsync<AccountInfo>(AccountPaths.Account);
        Assert.Equal(CharacterStatus.NeedsReauth, account!.Characters.Single().Status);

        // Logging in again restores it.
        await driver.DesktopLoginAsync(FakeEveSso.Character(login.CharacterId, "Revoked"));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(AccountPaths.AccessToken(login.CharacterId), null)).StatusCode);
    }

    [Fact]
    public async Task Transfer_detected_on_refresh_removes_the_character()
    {
        _factory.Sso.AccessTokenLifetime = TimeSpan.FromSeconds(30);
        var driver = new LoginDriver(_factory);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Sold"));
        _factory.Sso.OwnerOverrides[login.CharacterId] = "new-owner";
        var client = driver.AuthorizedClient(login.SessionToken);

        var response = await client.PostAsync(AccountPaths.AccessToken(login.CharacterId), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<AccountInfo>(AccountPaths.Account))!.Characters);
    }

    [Fact]
    public async Task Refresh_tokens_are_encrypted_at_rest()
    {
        var login = await new LoginDriver(_factory).DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Secret"));

        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<HuntersDbContext>().Characters.SingleAsync(c => c.Id == login.CharacterId);

        Assert.DoesNotContain("rt-", stored.EncryptedRefreshToken);
        Assert.DoesNotContain("at-", stored.EncryptedAccessToken);
    }

    private async Task<string> StoredRefreshTokenAsync(long characterId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HuntersDbContext>();
        var character = await db.Characters.AsNoTracking().SingleAsync(c => c.Id == characterId);
        return scope.ServiceProvider.GetRequiredService<TokenProtector>().Unprotect(character.EncryptedRefreshToken)!;
    }
}

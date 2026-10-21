using System.Net;
using System.Net.Http.Json;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Tests.Features.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Accounts;

[Collection(PostgresCollection.Name)]
public sealed class AccountTests(PostgresFixture postgres) : IDisposable
{
    private readonly HuntersServerFactory _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    private async Task<(DesktopLoginResult Main, long AltId, HttpClient Client)> AccountWithAltAsync()
    {
        var driver = new LoginDriver(_factory);
        var main = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Main"));
        var altId = TestIds.Character();
        await driver.DesktopLoginAsync(FakeEveSso.Character(altId, "Alt"), bearer: main.SessionToken);
        return (main, altId, driver.AuthorizedClient(main.SessionToken));
    }

    [Fact]
    public async Task Main_character_can_be_changed()
    {
        var (_, altId, client) = await AccountWithAltAsync();

        var response = await client.PutAsJsonAsync(AccountPaths.MainCharacter, new SetMainCharacterRequest(altId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var account = await client.GetFromJsonAsync<AccountInfo>(AccountPaths.Account);
        Assert.Equal(altId, account!.MainCharacterId);
        Assert.True(account.Characters[0].IsMain);
    }

    [Fact]
    public async Task Removing_a_character_revokes_its_token()
    {
        var (_, altId, client) = await AccountWithAltAsync();
        var revokedBefore = _factory.Sso.Revoked.Count;

        var response = await client.DeleteAsync(AccountPaths.Character(altId));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(revokedBefore + 1, _factory.Sso.Revoked.Count);
        Assert.Single((await client.GetFromJsonAsync<AccountInfo>(AccountPaths.Account))!.Characters);
    }

    [Fact]
    public async Task Main_cannot_be_removed_while_other_characters_exist()
    {
        var (main, _, client) = await AccountWithAltAsync();

        var response = await client.DeleteAsync(AccountPaths.Character(main.CharacterId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Characters_of_other_accounts_are_not_found()
    {
        var (_, _, client) = await AccountWithAltAsync();
        var stranger = await new LoginDriver(_factory).DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Stranger"));

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(AccountPaths.Character(stranger.CharacterId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync(AccountPaths.AccessToken(stranger.CharacterId), null)).StatusCode);
    }

    [Fact]
    public async Task Sessions_can_be_listed_and_revoked()
    {
        var driver = new LoginDriver(_factory);
        var identity = FakeEveSso.Character(TestIds.Character(), "Two Devices");
        var laptop = await driver.DesktopLoginAsync(identity);
        var desktop = await driver.DesktopLoginAsync(identity);
        var client = driver.AuthorizedClient(desktop.SessionToken);

        var sessions = await client.GetFromJsonAsync<List<SessionInfo>>(AccountPaths.Sessions);
        Assert.Equal(2, sessions!.Count);
        Assert.Single(sessions, s => s.IsCurrent);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(AccountPaths.Session(laptop.SessionId))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await driver.AuthorizedClient(laptop.SessionToken).GetAsync(AccountPaths.Account)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountPaths.Account)).StatusCode);
    }

    [Fact]
    public async Task Deleting_the_account_revokes_everything()
    {
        var (main, _, client) = await AccountWithAltAsync();
        var revokedBefore = _factory.Sso.Revoked.Count;

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(AccountPaths.Account)).StatusCode);

        Assert.Equal(revokedBefore + 2, _factory.Sso.Revoked.Count);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(AccountPaths.Account)).StatusCode);

        // The characters are free to start a new account.
        var again = await new LoginDriver(_factory).DesktopLoginAsync(FakeEveSso.Character(main.CharacterId, "Main"));
        Assert.True(again.IsNewAccount);
    }

    [Fact]
    public async Task Missing_scopes_are_reported()
    {
        var driver = new LoginDriver(_factory);
        var login = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Old Scopes", scopes: ["esi-location.read_location.v1"]));

        var character = Assert.Single(login.Account.Characters);

        Assert.Equal(CharacterStatus.MissingScopes, character.Status);
        Assert.Contains("esi-ui.write_waypoint.v1", character.MissingScopes);
    }
}

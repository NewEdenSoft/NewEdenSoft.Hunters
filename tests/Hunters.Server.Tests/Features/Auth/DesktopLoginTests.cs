using System.Net;
using System.Net.Http.Json;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Auth;

[Collection(PostgresCollection.Name)]
public sealed class DesktopLoginTests(PostgresFixture postgres) : IDisposable
{
    private readonly HuntersServerFactory _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task First_login_creates_account_with_main_character()
    {
        var id = TestIds.Character();
        var driver = new LoginDriver(_factory);

        var result = await driver.DesktopLoginAsync(FakeEveSso.Character(id, "Kira Vasstrom"));

        Assert.True(result.IsNewAccount);
        Assert.StartsWith("hs_", result.SessionToken);
        Assert.Equal(id, result.Account.MainCharacterId);
        var character = Assert.Single(result.Account.Characters);
        Assert.Equal("Kira Vasstrom", character.Name);
        Assert.Equal(CharacterStatus.Active, character.Status);

        var account = await driver.AuthorizedClient(result.SessionToken).GetFromJsonAsync<AccountInfo>(AccountPaths.Account);
        Assert.Equal(result.Account.Id, account!.Id);
    }

    [Fact]
    public async Task Logging_in_again_signs_in_to_the_same_account()
    {
        var identity = FakeEveSso.Character(TestIds.Character(), "Tamsin Oreval");
        var driver = new LoginDriver(_factory);

        var first = await driver.DesktopLoginAsync(identity);
        var second = await driver.DesktopLoginAsync(identity);

        Assert.False(second.IsNewAccount);
        Assert.Equal(first.Account.Id, second.Account.Id);
        Assert.NotEqual(first.SessionToken, second.SessionToken);
    }

    [Fact]
    public async Task Login_code_is_single_use()
    {
        var driver = new LoginDriver(_factory);
        var attempt = await driver.DesktopUntilLoopbackAsync(FakeEveSso.Character(TestIds.Character(), "Rook"));

        Assert.Equal(HttpStatusCode.OK, (await driver.RedeemAsync(attempt)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await driver.RedeemAsync(attempt)).StatusCode);
    }

    [Fact]
    public async Task Redeem_requires_the_app_verifier()
    {
        var driver = new LoginDriver(_factory);
        var attempt = await driver.DesktopUntilLoopbackAsync(FakeEveSso.Character(TestIds.Character(), "Rook"));

        var response = await driver.RedeemAsync(attempt, verifier: "not-the-verifier");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(AuthErrorCodes.InvalidLoginCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Browser_link_works_once()
    {
        var client = _factory.CreateNonRedirectingClient();
        var begin = await client.PostAsJsonAsync(AuthPaths.DesktopBegin, new BeginDesktopLoginRequest(50000, new string('a', 43)));
        var started = (await begin.Content.ReadFromJsonAsync<BeginDesktopLoginResponse>())!;
        var path = new Uri(started.BrowserUrl).PathAndQuery;

        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Callback_in_a_different_browser_is_rejected()
    {
        // An attacker starts a login and sends the callback link to someone else: the other
        // browser lacks the correlation cookie, so the login goes nowhere.
        var attackerBrowser = _factory.CreateNonRedirectingClient();
        var begin = await attackerBrowser.PostAsJsonAsync(AuthPaths.DesktopBegin, new BeginDesktopLoginRequest(50000, new string('a', 43)));
        var started = (await begin.Content.ReadFromJsonAsync<BeginDesktopLoginResponse>())!;
        var toEve = await attackerBrowser.GetAsync(new Uri(started.BrowserUrl).PathAndQuery);
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(toEve.Headers.Location!.Query)["state"].ToString();
        var code = _factory.Sso.IssueCode(FakeEveSso.Character(TestIds.Character(), "Attacker"));

        var victimBrowser = _factory.CreateNonRedirectingClient();
        var callback = await victimBrowser.GetAsync($"{AuthPaths.EveCallback}?code={code}&state={Uri.EscapeDataString(state)}");

        Assert.NotEqual(HttpStatusCode.OK, callback.StatusCode);
        Assert.DoesNotContain("code=", callback.Headers.Location?.Query ?? "");
        Assert.Null(_factory.Sso.LastRedirectUri); // the code was never exchanged
    }

    [Fact]
    public async Task Eve_redirect_uri_is_the_configured_callback()
    {
        var driver = new LoginDriver(_factory);
        var client = _factory.CreateNonRedirectingClient();
        var begin = await client.PostAsJsonAsync(AuthPaths.DesktopBegin, new BeginDesktopLoginRequest(50000, new string('a', 43)));
        var started = (await begin.Content.ReadFromJsonAsync<BeginDesktopLoginResponse>())!;

        var toEve = await client.GetAsync(new Uri(started.BrowserUrl).PathAndQuery);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(toEve.Headers.Location!.Query);

        Assert.StartsWith("https://login.eveonline.com/v2/oauth/authorize", toEve.Headers.Location.ToString());
        Assert.Equal("http://localhost/auth/eve/callback", query["redirect_uri"]);
        Assert.Equal("test-client", query["client_id"]);
        Assert.Contains("esi-location.read_location.v1", query["scope"].ToString());
        Assert.False(query.ContainsKey("code_challenge"));

        await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Anyone"));
        Assert.Equal("http://localhost/auth/eve/callback", _factory.Sso.LastRedirectUri);
    }

    [Fact]
    public async Task Browser_link_uses_the_public_origin_of_the_callback()
    {
        // The app reaches the server at its own address (here the test host); the browser must
        // go to the public address EVE returns to, e.g. the Vite dev server or a reverse proxy.
        using var factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString)
            .With("Hunters:Sso:CallbackUrl", "https://hunters.example.com/auth/eve/callback");

        var begin = await factory.CreateClient().PostAsJsonAsync(AuthPaths.DesktopBegin, new BeginDesktopLoginRequest(50000, new string('a', 43)));
        var started = (await begin.Content.ReadFromJsonAsync<BeginDesktopLoginResponse>())!;

        Assert.Equal($"https://hunters.example.com/auth/desktop/{started.LoginId}", started.BrowserUrl);
    }

    [Fact]
    public async Task Callback_with_unknown_state_shows_expired_page()
    {
        var response = await _factory.CreateNonRedirectingClient().GetAsync($"{AuthPaths.EveCallback}?code=x&state=unknown");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("expired", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(80)]
    [InlineData(70000)]
    public async Task Begin_rejects_invalid_ports(int port)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(AuthPaths.DesktopBegin, new BeginDesktopLoginRequest(port, new string('a', 43)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Adding_a_character_links_it_to_the_signed_in_account()
    {
        var driver = new LoginDriver(_factory);
        var main = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Main"));
        var altId = TestIds.Character();

        var added = await driver.DesktopLoginAsync(FakeEveSso.Character(altId, "Alt"), bearer: main.SessionToken);

        Assert.False(added.IsNewAccount);
        Assert.Equal(main.Account.Id, added.Account.Id);
        Assert.Equal(2, added.Account.Characters.Count);
        Assert.Equal(main.Account.MainCharacterId, added.Account.MainCharacterId);
    }

    [Fact]
    public async Task Character_linked_to_another_account_cannot_be_added()
    {
        var driver = new LoginDriver(_factory);
        var other = FakeEveSso.Character(TestIds.Character(), "Taken");
        await driver.DesktopLoginAsync(other);
        var mine = await driver.DesktopLoginAsync(FakeEveSso.Character(TestIds.Character(), "Mine"));

        var attempt = await driver.DesktopUntilLoopbackAsync(other, bearer: mine.SessionToken);

        Assert.Equal(AuthErrorCodes.CharacterLinkedElsewhere, attempt.Error);
        Assert.NotEmpty(_factory.Sso.Revoked);
    }

    [Fact]
    public async Task Transferred_character_moves_to_a_new_account()
    {
        var id = TestIds.Character();
        var driver = new LoginDriver(_factory);
        var original = await driver.DesktopLoginAsync(FakeEveSso.Character(id, "Sold", owner: "seller"));

        var buyer = await driver.DesktopLoginAsync(FakeEveSso.Character(id, "Sold", owner: "buyer"));

        Assert.True(buyer.IsNewAccount);
        Assert.NotEqual(original.Account.Id, buyer.Account.Id);
        var sellerAccount = await driver.AuthorizedClient(original.SessionToken).GetFromJsonAsync<AccountInfo>(AccountPaths.Account);
        Assert.Empty(sellerAccount!.Characters);
    }

    [Fact]
    public async Task Api_requires_a_valid_session()
    {
        var driver = new LoginDriver(_factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(AccountPaths.Account)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await driver.AuthorizedClient("hs_bogus").GetAsync(AccountPaths.Account)).StatusCode);
    }
}

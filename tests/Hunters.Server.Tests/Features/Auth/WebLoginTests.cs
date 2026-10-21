using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Auth;

[Collection(PostgresCollection.Name)]
public sealed class WebLoginTests(PostgresFixture postgres) : IDisposable
{
    private readonly HuntersServerFactory _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Web_login_sets_a_session_cookie_and_returns_to_the_page()
    {
        var browser = _factory.CreateNonRedirectingClient();

        var callback = await LoginAsync(browser, FakeEveSso.Character(TestIds.Character(), "Web User"), returnUrl: "/account");

        Assert.Equal("/account", callback.Headers.Location!.OriginalString);
        Assert.Contains(callback.Headers.GetValues("Set-Cookie"), c => c.StartsWith("hunters.session="));

        var account = await browser.GetFromJsonAsync<AccountInfo>(AccountPaths.Account);
        Assert.Equal("Web User", Assert.Single(account!.Characters).Name);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    public async Task External_return_urls_are_ignored(string returnUrl)
    {
        var callback = await LoginAsync(_factory.CreateNonRedirectingClient(), FakeEveSso.Character(TestIds.Character(), "Web User"), returnUrl);

        Assert.Equal("/", callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Login_csrf_is_blocked_by_the_correlation_cookie()
    {
        // The attacker completes a login with their own character but hands the callback to a
        // victim: without the correlation cookie the victim must not end up signed in.
        var attacker = _factory.CreateNonRedirectingClient();
        var start = await attacker.PostAsync(AuthPaths.WebLogin, new FormUrlEncodedContent([]));
        var state = QueryHelpers.ParseQuery(start.Headers.Location!.Query)["state"];
        var code = _factory.Sso.IssueCode(FakeEveSso.Character(TestIds.Character(), "Attacker"));

        var victim = _factory.CreateNonRedirectingClient();
        var callback = await victim.GetAsync($"{AuthPaths.EveCallback}?code={code}&state={Uri.EscapeDataString(state!)}");

        Assert.False(callback.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("hunters.session=")));
        Assert.Equal(HttpStatusCode.Unauthorized, (await victim.GetAsync(AccountPaths.Account)).StatusCode);
    }

    [Fact]
    public async Task Logout_ends_the_session()
    {
        var browser = _factory.CreateNonRedirectingClient();
        await LoginAsync(browser, FakeEveSso.Character(TestIds.Character(), "Web User"));

        await browser.PostAsync(AuthPaths.WebLogout, null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync(AccountPaths.Account)).StatusCode);
    }

    [Fact]
    public async Task Failed_web_login_returns_to_the_login_page_with_the_error()
    {
        var browser = _factory.CreateNonRedirectingClient();
        var start = await browser.GetAsync(AuthPaths.WebLogin);
        var state = QueryHelpers.ParseQuery(start.Headers.Location!.Query)["state"];

        var callback = await browser.GetAsync($"{AuthPaths.EveCallback}?error=access_denied&state={Uri.EscapeDataString(state!)}");

        Assert.StartsWith($"/login?error={AuthErrorCodes.LoginCancelled}", callback.Headers.Location!.OriginalString);
    }

    private async Task<HttpResponseMessage> LoginAsync(HttpClient browser, EveIdentity identity, string? returnUrl = null)
    {
        var start = await browser.PostAsync(AuthPaths.WebLogin, new FormUrlEncodedContent(
            returnUrl is null ? [] : [new KeyValuePair<string, string>("returnUrl", returnUrl)]));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var state = QueryHelpers.ParseQuery(start.Headers.Location!.Query)["state"];

        var callback = await browser.GetAsync($"{AuthPaths.EveCallback}?code={_factory.Sso.IssueCode(identity)}&state={Uri.EscapeDataString(state!)}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        return callback;
    }
}

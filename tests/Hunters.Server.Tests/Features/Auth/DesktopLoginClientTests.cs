using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Microsoft.AspNetCore.WebUtilities;
using NewEdenSoft.Hunters.Client.Auth;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;
using NewEdenSoft.Hunters.Server.Tests.Fakes;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Auth;

/// <summary>
/// The desktop app's <see cref="DesktopLoginClient"/> against the in-memory server. The loopback
/// listener is real; <see cref="FakeBrowser"/> plays the browser and the EVE login page.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DesktopLoginClientTests(PostgresFixture postgres) : IDisposable
{
    private static readonly Uri Server = new("http://localhost/");

    private readonly HuntersServerFactory _factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Login_returns_a_working_session()
    {
        var browser = new FakeBrowser(_factory, FakeEveSso.Character(TestIds.Character(), "Ines Vaarlo")) { OpenSpareConnections = true };

        var result = await NewClient(browser).LoginAsync(Server, deviceName: "Test PC");

        Assert.True(result.IsNewAccount);
        Assert.Equal("Ines Vaarlo", Assert.Single(result.Account.Characters).Name);
        Assert.Contains("Login complete", await browser.LoopbackPage!);
        var account = await new LoginDriver(_factory).AuthorizedClient(result.SessionToken).GetFromJsonAsync<AccountInfo>(AccountPaths.Account);
        Assert.Equal(result.Account.Id, account!.Id);
    }

    [Fact]
    public async Task Refused_login_reports_the_server_error_in_app_and_browser()
    {
        using var factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString)
            .With("Hunters:Admission:AccessKey:Enabled", "true");
        var browser = new FakeBrowser(factory, FakeEveSso.Character(TestIds.Character(), "No Key"));

        var error = await Assert.ThrowsAsync<HuntersLoginException>(() => NewClient(browser, factory).LoginAsync(Server));

        Assert.Equal(AuthErrorCodes.AccessKeyRequired, error.Code);
        Assert.Contains("Login failed", await browser.LoopbackPage!);
    }

    [Fact]
    public async Task Invalid_access_key_fails_before_the_browser_opens()
    {
        using var factory = new HuntersServerFactory().WithDatabase(postgres.ConnectionString)
            .With("Hunters:Admission:AccessKey:Enabled", "true");
        var browser = new FakeBrowser(factory, FakeEveSso.Character(TestIds.Character(), "Wrong Key"));

        var error = await Assert.ThrowsAsync<HuntersLoginException>(() => NewClient(browser, factory).LoginAsync(Server, accessKey: "nope"));

        Assert.Equal(AuthErrorCodes.AccessKeyInvalid, error.Code);
        Assert.False(browser.WasOpened);
    }

    [Fact]
    public async Task Cancelling_at_eve_reports_cancelled()
    {
        var browser = new FakeBrowser(_factory, identity: null);

        var error = await Assert.ThrowsAsync<HuntersLoginException>(() => NewClient(browser).LoginAsync(Server));

        Assert.Equal(AuthErrorCodes.LoginCancelled, error.Code);
    }

    [Fact]
    public async Task Unreachable_server_is_reported()
    {
        var client = new DesktopLoginClient(new HttpClient(), new FakeBrowser(_factory, identity: null));

        var error = await Assert.ThrowsAsync<HuntersLoginException>(() => client.LoginAsync(new Uri($"http://127.0.0.1:{UnusedPort()}/")));

        Assert.Equal(HuntersLoginException.ServerUnreachable, error.Code);
    }

    private DesktopLoginClient NewClient(FakeBrowser browser, HuntersServerFactory? factory = null) =>
        new((factory ?? _factory).CreateClient(), browser);

    private static int UnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Follows the browser URL through the server and the (fake) EVE login, then delivers the
    /// server's redirect to the app's real loopback listener. Without an identity the user
    /// cancels at EVE.
    /// </summary>
    private sealed class FakeBrowser(HuntersServerFactory factory, EveIdentity? identity) : IBrowserLauncher
    {
        private readonly HttpClient _browser = factory.CreateNonRedirectingClient();

        public bool WasOpened => LoopbackPage is not null;

        /// <summary>Opens an idle connection and requests a favicon first, as real browsers do.</summary>
        public bool OpenSpareConnections { get; init; }

        /// <summary>The page the app's listener showed at the end, once the browser has read it.</summary>
        public Task<string>? LoopbackPage { get; private set; }

        public Task OpenAsync(Uri url, CancellationToken cancellationToken)
        {
            // The app waits on its listener after opening the browser, so the browser runs on its own.
            LoopbackPage = Task.Run(() => BrowseAsync(url), CancellationToken.None);
            return Task.CompletedTask;
        }

        private async Task<string> BrowseAsync(Uri url)
        {
            var toEve = await _browser.GetAsync(url.PathAndQuery);
            var state = Uri.EscapeDataString(QueryHelpers.ParseQuery(toEve.Headers.Location!.Query)["state"].ToString());

            var callback = await _browser.GetAsync(identity is null
                ? $"{AuthPaths.EveCallback}?error=access_denied&state={state}"
                : $"{AuthPaths.EveCallback}?code={factory.Sso.IssueCode(identity)}&state={state}");
            var loopback = callback.Headers.Location!;

            using var real = new HttpClient();
            using var idle = new TcpClient();
            if (OpenSpareConnections)
            {
                await idle.ConnectAsync(IPAddress.Loopback, loopback.Port);
                Assert.Equal(HttpStatusCode.NotFound, (await real.GetAsync(new Uri(loopback, "/favicon.ico"))).StatusCode);
            }

            return await real.GetStringAsync(loopback);
        }
    }
}

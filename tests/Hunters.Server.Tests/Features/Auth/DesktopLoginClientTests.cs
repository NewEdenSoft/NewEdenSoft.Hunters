using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using NewEdenSoft.Hunters.Client.Auth;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Auth;
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
}

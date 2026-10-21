using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.WebUtilities;
using NewEdenSoft.Hunters.Client.Auth;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Tests.Fakes;

/// <summary>
/// Plays the user's browser for <see cref="DesktopLoginClient"/>: follows the browser URL through
/// the in-memory server and the (fake) EVE login, then delivers the server's redirect to the app's
/// real loopback listener. With <see cref="Identity"/> null the user cancels at EVE.
/// </summary>
public sealed class FakeBrowser(HuntersServerFactory factory, EveIdentity? identity) : IBrowserLauncher
{
    private readonly HttpClient _browser = factory.CreateNonRedirectingClient();

    /// <summary>The character the user logs in with at EVE on the next visit.</summary>
    public EveIdentity? Identity { get; set; } = identity;

    public bool WasOpened { get; private set; }

    /// <summary>The user never finishes in the browser (closed the tab, or the browser shows an error page).</summary>
    public bool NeverFinishes { get; init; }

    /// <summary>Opens an idle connection and requests a favicon first, as real browsers do.</summary>
    public bool OpenSpareConnections { get; init; }

    /// <summary>The page the app's listener showed at the end, once the browser has read it.</summary>
    public Task<string>? LoopbackPage { get; private set; }

    public Task OpenAsync(Uri url, CancellationToken cancellationToken)
    {
        WasOpened = true;
        if (NeverFinishes)
            return Task.CompletedTask;

        // The app waits on its listener after opening the browser, so the browser runs on its own.
        var identity = Identity;
        LoopbackPage = Task.Run(() => BrowseAsync(url, identity), CancellationToken.None);
        return Task.CompletedTask;
    }

    private async Task<string> BrowseAsync(Uri url, EveIdentity? identity)
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

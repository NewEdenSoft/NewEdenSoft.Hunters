using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Server.Features.Auth;
using NewEdenSoft.Hunters.Server.Infrastructure.Eve;

namespace NewEdenSoft.Hunters.Server.Tests.Features.Auth;

/// <summary>Steps through the desktop and web login flows the way the app and browser would.</summary>
public sealed class LoginDriver(HuntersServerFactory factory)
{
    private const int LoopbackPort = 50123;

    public sealed record DesktopAttempt(string LoginId, string Verifier, Uri LoopbackRedirect)
    {
        public string? Code => Query("code");
        public string? Error => Query("error");

        private string? Query(string key) =>
            QueryHelpers.ParseQuery(LoopbackRedirect.Query).TryGetValue(key, out var v) ? v.ToString() : null;
    }

    /// <summary>Begin, open the browser link, log in at (fake) EVE, follow the callback to the loopback redirect.</summary>
    public async Task<DesktopAttempt> DesktopUntilLoopbackAsync(EveIdentity identity, string? accessKey = null, string? bearer = null)
    {
        var client = factory.CreateNonRedirectingClient();
        var verifier = Secrets.NewToken();

        using var begin = new HttpRequestMessage(HttpMethod.Post, AuthPaths.DesktopBegin)
        {
            Content = JsonContent.Create(new BeginDesktopLoginRequest(LoopbackPort, Secrets.Sha256Base64Url(verifier), accessKey, "Test PC")),
        };
        if (bearer is not null)
            begin.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        var beginResponse = await client.SendAsync(begin);
        Assert.Equal(HttpStatusCode.OK, beginResponse.StatusCode);
        var started = (await beginResponse.Content.ReadFromJsonAsync<BeginDesktopLoginResponse>())!;

        var browser = await client.GetAsync(new Uri(started.BrowserUrl).PathAndQuery);
        Assert.Equal(HttpStatusCode.Redirect, browser.StatusCode);
        var state = QueryHelpers.ParseQuery(browser.Headers.Location!.Query)["state"].ToString();

        var callback = await client.GetAsync($"{AuthPaths.EveCallback}?code={factory.Sso.IssueCode(identity)}&state={Uri.EscapeDataString(state)}");
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        var loopback = callback.Headers.Location!;
        Assert.Equal($"http://127.0.0.1:{LoopbackPort}{AuthPaths.LoopbackCallback}", loopback.GetLeftPart(UriPartial.Path));

        return new DesktopAttempt(started.LoginId, verifier, loopback);
    }

    public async Task<HttpResponseMessage> RedeemAsync(DesktopAttempt attempt, string? verifier = null) =>
        await factory.CreateClient().PostAsJsonAsync(AuthPaths.DesktopRedeem,
            new RedeemDesktopLoginRequest(attempt.LoginId, attempt.Code!, verifier ?? attempt.Verifier));

    /// <summary>The full desktop login. Fails the test if it does not succeed.</summary>
    public async Task<DesktopLoginResult> DesktopLoginAsync(EveIdentity identity, string? accessKey = null, string? bearer = null)
    {
        var attempt = await DesktopUntilLoopbackAsync(identity, accessKey, bearer);
        Assert.Null(attempt.Error);
        var redeem = await RedeemAsync(attempt);
        Assert.Equal(HttpStatusCode.OK, redeem.StatusCode);
        return (await redeem.Content.ReadFromJsonAsync<DesktopLoginResult>())!;
    }

    public HttpClient AuthorizedClient(string sessionToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionToken);
        return client;
    }
}

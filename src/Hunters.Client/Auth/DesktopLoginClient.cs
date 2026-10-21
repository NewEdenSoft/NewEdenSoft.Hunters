using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using NewEdenSoft.Hunters.Client.Http;
using NewEdenSoft.Hunters.Contracts.Auth;

namespace NewEdenSoft.Hunters.Client.Auth;

/// <summary>
/// Desktop login against any Hunters server: listen on a loopback port, register the login with
/// the server, open the browser, wait for the server's redirect with a single-use code and redeem
/// it with this app's verifier. See docs/authentication.md, "Desktop login".
/// </summary>
public sealed class DesktopLoginClient(HttpClient http, IBrowserLauncher browser)
{
    /// <summary>How long to wait for the user to finish in the browser (EVE login, 2FA, scope approval).</summary>
    public static readonly TimeSpan BrowserTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Runs a full login. With <paramref name="sessionToken"/> the character is added to that
    /// session's account instead.
    /// </summary>
    /// <exception cref="HuntersLoginException">The server or EVE refused the login, or it timed out.</exception>
    public async Task<DesktopLoginResult> LoginAsync(
        Uri server,
        string? accessKey = null,
        string? deviceName = null,
        string? sessionToken = null,
        CancellationToken cancellationToken = default)
    {
        await using var listener = LoopbackListener.Start();
        var verifier = NewVerifier();

        using var begin = new HttpRequestMessage(HttpMethod.Post, new Uri(server, AuthPaths.DesktopBegin))
        {
            Content = JsonContent.Create(new BeginDesktopLoginRequest(
                listener.Port, Challenge(verifier), string.IsNullOrWhiteSpace(accessKey) ? null : accessKey.Trim(), deviceName)),
        };
        if (sessionToken is not null)
            begin.Headers.Authorization = new("Bearer", sessionToken);

        var started = await SendAsync<BeginDesktopLoginResponse>(begin, cancellationToken);

        await browser.OpenAsync(new Uri(started.BrowserUrl), cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(BrowserTimeout);
        IReadOnlyDictionary<string, string> callback;
        try
        {
            callback = await listener.WaitForCallbackAsync(LoopbackPages.Render, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HuntersLoginException(AuthErrorCodes.LoginExpired, "The login was not finished in the browser in time.");
        }

        if (callback.GetValueOrDefault("loginId") != started.LoginId)
            throw new HuntersLoginException(AuthErrorCodes.InvalidLoginCode, "The browser returned a different login. Try again.");
        if (callback.TryGetValue("error", out var error))
            throw new HuntersLoginException(error, callback.GetValueOrDefault("error_description") ?? "The login failed.");
        if (callback.GetValueOrDefault("code") is not { Length: > 0 } code)
            throw new HuntersLoginException(AuthErrorCodes.InvalidLoginCode, "The browser returned no login code. Try again.");

        using var redeem = new HttpRequestMessage(HttpMethod.Post, new Uri(server, AuthPaths.DesktopRedeem))
        {
            Content = JsonContent.Create(new RedeemDesktopLoginRequest(started.LoginId, code, verifier)),
        };
        return await SendAsync<DesktopLoginResult>(redeem, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw new HuntersLoginException(HuntersLoginException.ServerUnreachable, $"Could not reach the server: {e.Message}", e);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw await ToExceptionAsync(response, cancellationToken);

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                   ?? throw new HuntersLoginException(HuntersLoginException.UnexpectedResponse, "The server sent an empty response.");
        }
    }

    private static async Task<HuntersLoginException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await ServerProblem.ReadAsync(response, cancellationToken);
        if (problem.Code is not null)
            return new HuntersLoginException(problem.Code, problem.Title ?? "The server refused the login.");

        return response.StatusCode == HttpStatusCode.NotFound
            ? new HuntersLoginException(HuntersLoginException.UnexpectedResponse, "This address does not look like a Hunters server.")
            : new HuntersLoginException(HuntersLoginException.UnexpectedResponse, $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.");
    }

    /// <summary>32 random bytes, base64url: 43 characters.</summary>
    private static string NewVerifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    internal static string Challenge(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
}

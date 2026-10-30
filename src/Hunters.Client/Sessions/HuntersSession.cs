using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NewEdenSoft.Hunters.Client.Auth;
using NewEdenSoft.Hunters.Client.Http;
using NewEdenSoft.Hunters.Contracts.Account;
using NewEdenSoft.Hunters.Contracts.Activity;
using NewEdenSoft.Hunters.Contracts.Auth;
using NewEdenSoft.Hunters.Contracts.Discovery;
using NewEdenSoft.Hunters.Contracts.Hunting;
using NewEdenSoft.Hunters.Contracts.Intel;
using NewEdenSoft.Hunters.Contracts.Map;

namespace NewEdenSoft.Hunters.Client.Sessions;

/// <summary>
/// A signed-in session on one Hunters server: the account snapshot, character management and
/// access tokens. Kept in memory only (docs/decisions.md, D20).
/// </summary>
/// <remarks>
/// Events are raised on the thread that caused the change; UI code marshals them itself.
/// </remarks>
public sealed class HuntersSession : ICharacterTokenProvider
{
    /// <summary>Cached access tokens are replaced this long before they expire.</summary>
    public static readonly TimeSpan TokenMargin = TimeSpan.FromSeconds(60);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly Lock _lock = new();
    private readonly Dictionary<long, AccessTokenResponse> _tokens = [];
    private string _sessionToken;

    public HuntersSession(HttpClient http, Uri server, DesktopLoginResult login, TimeProvider? time = null)
    {
        _http = http;
        _time = time ?? TimeProvider.System;
        _sessionToken = login.SessionToken;
        Server = server;
        SessionId = login.SessionId;
        Account = login.Account;
    }

    public Uri Server { get; }

    public Guid SessionId { get; private set; }

    public AccountInfo Account { get; private set; }

    public bool IsEnded { get; private set; }

    /// <summary>The account snapshot changed: characters added, removed, re-authed or marked as needing a new login.</summary>
    public event EventHandler? AccountChanged;

    /// <summary>The session ended: logged out, account deleted, or rejected by the server.</summary>
    public event EventHandler? Ended;

    public async Task RefreshAsync(CancellationToken cancellationToken = default) =>
        SetAccount(await SendAsync<AccountInfo>(HttpMethod.Get, AccountPaths.Account, cancellationToken: cancellationToken));

    /// <summary>
    /// Logs in with another character in the browser and adds it to this account. Logging in with a
    /// character that is already on the account renews its token, which is how re-auth works.
    /// </summary>
    /// <returns>The login result; <see cref="DesktopLoginResult.CharacterId"/> is the character the user picked at EVE.</returns>
    public async Task<DesktopLoginResult> AddCharacterAsync(DesktopLoginClient logins, string? deviceName = null, CancellationToken cancellationToken = default)
    {
        var result = await logins.LoginAsync(Server, deviceName: deviceName, sessionToken: _sessionToken, cancellationToken: cancellationToken);

        // Every desktop login issues a new session; switch to it and end the one it replaces.
        Guid previous;
        lock (_lock)
        {
            previous = SessionId;
            _sessionToken = result.SessionToken;
            SessionId = result.SessionId;
            _tokens.Remove(result.CharacterId);
        }

        SetAccount(result.Account);
        if (previous != result.SessionId)
            await TryAsync(() => SendAsync(HttpMethod.Delete, AccountPaths.Session(previous), cancellationToken: cancellationToken));
        return result;
    }

    /// <summary>The server's discovery document; it names the current map pack.</summary>
    public Task<DiscoveryDocument> GetDiscoveryAsync(CancellationToken cancellationToken = default) =>
        SendAsync<DiscoveryDocument>(HttpMethod.Get, DiscoveryDocument.Path, cancellationToken: cancellationToken);

    /// <summary>The map pack of one SDE build in a format, as named by <see cref="DiscoveryDocument.MapPack"/>.</summary>
    public Task<MapPack> GetMapPackAsync(long sdeBuild, int format = MapPack.Format, CancellationToken cancellationToken = default) =>
        SendAsync<MapPack>(HttpMethod.Get, MapPaths.Pack(sdeBuild, format), cancellationToken: cancellationToken);

    /// <summary>
    /// Activity now for the whole map. It changes once an hour, so pass the last answer: the server then
    /// answers 304 until there is a new hour, and the same instance comes back.
    /// </summary>
    public Task<Revalidated<ActivityCurrent>> GetActivityAsync(Revalidated<ActivityCurrent>? previous, CancellationToken cancellationToken = default) =>
        GetRevalidatedAsync(ActivityPaths.Current, previous, cancellationToken);

    /// <summary>The server's hunting profiles: the kinds of target it finds.</summary>
    public Task<IReadOnlyList<HuntingProfileInfo>> GetHuntingProfilesAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<HuntingProfileInfo>>(HttpMethod.Get, HuntingPaths.Profiles, cancellationToken: cancellationToken);

    /// <summary>
    /// What is in reach of a system: every system within <paramref name="jumps"/> gate jumps and/or every
    /// cyno target within <paramref name="ly"/> light years, with its intel, sorted by <paramref name="profile"/>'s
    /// "likely now" or else by <paramref name="sort"/> (<see cref="ReachSorts"/>).
    /// </summary>
    public Task<IntelReach> GetReachAsync(
        int from, int? jumps, double? ly, string? profile, int limit, string sort, CancellationToken cancellationToken = default) =>
        SendAsync<IntelReach>(HttpMethod.Get, IntelPaths.Reach(from, jumps, ly, profile, limit, sort), cancellationToken: cancellationToken);

    /// <summary>Every hunting profile seen in one system, highest score first, with its regulars.</summary>
    public Task<HuntingPlace> GetSystemHuntingAsync(int systemId, CancellationToken cancellationToken = default) =>
        SendAsync<HuntingPlace>(HttpMethod.Get, HuntingPaths.System(systemId), cancellationToken: cancellationToken);

    /// <summary>Where every hunting profile is seen. Revalidated like <see cref="GetActivityAsync"/>.</summary>
    public Task<Revalidated<HuntingOverview>> GetHuntingOverviewAsync(Revalidated<HuntingOverview>? previous, CancellationToken cancellationToken = default) =>
        GetRevalidatedAsync(HuntingPaths.Overview, previous, cancellationToken);

    public async Task SetMainCharacterAsync(long characterId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Put, AccountPaths.MainCharacter, new SetMainCharacterRequest(characterId), cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    /// <summary>Removes the character from the account; the server revokes its token at EVE.</summary>
    public async Task RemoveCharacterAsync(long characterId, CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, AccountPaths.Character(characterId), cancellationToken: cancellationToken);
        lock (_lock)
            _tokens.Remove(characterId);
        await RefreshAsync(cancellationToken);
    }

    /// <summary>Deletes the account and everything in it on the server, and ends this session.</summary>
    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(HttpMethod.Delete, AccountPaths.Account, cancellationToken: cancellationToken);
        End();
    }

    /// <summary>Revokes this session on the server (best effort) and ends it.</summary>
    public async Task LogOutAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnded)
            await TryAsync(() => SendAsync(HttpMethod.Delete, AccountPaths.Session(SessionId), cancellationToken: cancellationToken));
        End();
    }

    public async ValueTask<string> GetAccessTokenAsync(long characterId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_tokens.TryGetValue(characterId, out var cached) && cached.ExpiresAt - TokenMargin > _time.GetUtcNow())
                return cached.AccessToken;
        }

        try
        {
            var token = await SendAsync<AccessTokenResponse>(HttpMethod.Post, AccountPaths.AccessToken(characterId), cancellationToken: cancellationToken);
            lock (_lock)
                _tokens[characterId] = token;
            return token.AccessToken;
        }
        catch (HuntersApiException e) when (e.Code == AuthErrorCodes.NeedsReauth)
        {
            MarkNeedsReauth(characterId);
            throw;
        }
    }

    public void InvalidateAccessToken(long characterId)
    {
        lock (_lock)
            _tokens.Remove(characterId);
    }

    private void MarkNeedsReauth(long characterId)
    {
        var account = Account;
        if (account.Characters.FirstOrDefault(c => c.Id == characterId) is not { Status: not CharacterStatus.NeedsReauth })
            return;

        SetAccount(account with
        {
            Characters = [.. account.Characters.Select(c => c.Id == characterId ? c with { Status = CharacterStatus.NeedsReauth } : c)],
        });
    }

    private void SetAccount(AccountInfo account)
    {
        Account = account;
        AccountChanged?.Invoke(this, EventArgs.Empty);
    }

    private void End()
    {
        lock (_lock)
        {
            if (IsEnded)
                return;
            IsEnded = true;
            _tokens.Clear();
        }

        Ended?.Invoke(this, EventArgs.Empty);
    }

    private static async Task TryAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (HuntersApiException)
        {
            // Best effort: an unreachable server or an already revoked session changes nothing for the user.
        }
    }

    private async Task SendAsync(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(method, path, body, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        using var response = await SendCoreAsync(method, path, body, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
               ?? throw new HuntersApiException(null, "The server sent an empty response.", response.StatusCode);
    }

    /// <summary>A GET revalidated with the ETag of the previous answer; on 304 the previous answer is returned.</summary>
    private async Task<Revalidated<T>> GetRevalidatedAsync<T>(string path, Revalidated<T>? previous, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await SendCoreAsync(HttpMethod.Get, path, null, cancellationToken, previous?.ETag);
        if (response.StatusCode == HttpStatusCode.NotModified && previous is not null)
            return previous;

        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                    ?? throw new HuntersApiException(null, "The server sent an empty response.", response.StatusCode);
        return new Revalidated<T>(value, response.Headers.ETag);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        HttpMethod method, string path, object? body, CancellationToken cancellationToken, EntityTagHeaderValue? ifNoneMatch = null)
    {
        if (IsEnded)
            throw new HuntersApiException(HuntersApiException.SessionEnded, "You are logged out.");

        using var request = new HttpRequestMessage(method, new Uri(Server, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _sessionToken);
        if (ifNoneMatch is not null)
            request.Headers.IfNoneMatch.Add(ifNoneMatch);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            throw new HuntersApiException(HuntersApiException.ServerUnreachable, $"Could not reach the server: {e.Message}", inner: e);
        }

        if (response.IsSuccessStatusCode || (ifNoneMatch is not null && response.StatusCode == HttpStatusCode.NotModified))
            return response;

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                End();
                throw new HuntersApiException(HuntersApiException.SessionEnded, "Your session has ended. Log in again.", response.StatusCode);
            }

            var problem = await ServerProblem.ReadAsync(response, cancellationToken);
            throw new HuntersApiException(problem.Code, problem.Title ?? $"The server answered {(int)response.StatusCode} {response.ReasonPhrase}.", response.StatusCode);
        }
    }
}

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NewEdenSoft.Hunters.Server.Configuration;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Eve;

public sealed partial class EveSsoClient(
    HttpClient http,
    EveSigningKeys signingKeys,
    IOptions<HuntersOptions> options,
    ILogger<EveSsoClient> logger) : IEveSsoClient
{
    private const string EveAudience = "EVE Online";
    private const string SubjectPrefix = "CHARACTER:EVE:";

    private SsoOptions Sso => options.Value.Sso;

    public Task<EveTokenSet> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken) =>
        RequestTokenAsync([new("grant_type", "authorization_code"), new("code", code), new("redirect_uri", redirectUri)], cancellationToken);

    public Task<EveTokenSet> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        RequestTokenAsync([new("grant_type", "refresh_token"), new("refresh_token", refreshToken)], cancellationToken);

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        try
        {
            using var request = NewRequest(Sso.RevokeUrl, [new("token_type_hint", "refresh_token"), new("token", refreshToken)]);
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                LogRevokeFailed(null, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogRevokeFailed(ex, null);
        }
    }

    private async Task<EveTokenSet> RequestTokenAsync(KeyValuePair<string, string>[] form, CancellationToken cancellationToken)
    {
        using var request = NewRequest(Sso.TokenUrl, form);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new EveSsoException("unavailable", "EVE SSO could not be reached.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var error = TryParse<TokenError>(body);
                throw new EveSsoException(
                    error?.Error ?? $"http_{(int)response.StatusCode}",
                    error?.Description ?? $"EVE SSO returned {(int)response.StatusCode}.");
            }

            var token = TryParse<TokenResponse>(body)
                ?? throw new EveSsoException("invalid_response", "EVE SSO returned an unreadable token response.");
            if (string.IsNullOrEmpty(token.AccessToken) || string.IsNullOrEmpty(token.RefreshToken))
                throw new EveSsoException("invalid_response", "EVE SSO returned an incomplete token response.");

            var (identity, expiresAt) = await ValidateAsync(token.AccessToken, cancellationToken);
            return new EveTokenSet(token.AccessToken, token.RefreshToken, expiresAt, identity);
        }
    }

    private HttpRequestMessage NewRequest(string url, KeyValuePair<string, string>[] form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Sso.ClientId}:{Sso.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return request;
    }

    private async Task<(EveIdentity Identity, DateTimeOffset ExpiresAt)> ValidateAsync(string accessToken, CancellationToken cancellationToken)
    {
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuers = Sso.Issuers,
            ValidAudiences = [Sso.ClientId, EveAudience],
            IssuerSigningKeys = await signingKeys.GetAsync(cancellationToken),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        });

        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
            throw new EveSsoException("invalid_token", "EVE SSO returned an access token that failed validation.", result.Exception);

        var subject = jwt.Subject;
        if (!subject.StartsWith(SubjectPrefix, StringComparison.Ordinal)
            || !long.TryParse(subject.AsSpan(SubjectPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var characterId))
        {
            throw new EveSsoException("invalid_token", $"Unexpected token subject '{subject}'.");
        }

        var scopes = jwt.Claims.Where(c => c.Type == "scp").Select(c => c.Value).ToList();
        var name = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? "";
        var owner = jwt.Claims.FirstOrDefault(c => c.Type == "owner")?.Value ?? "";

        return (new EveIdentity(characterId, name, owner, scopes), new DateTimeOffset(jwt.ValidTo, TimeSpan.Zero));
    }

    private static T? TryParse<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Revoking an EVE refresh token failed (status {Status})")]
    private partial void LogRevokeFailed(Exception? exception, int? status);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);

    private sealed record TokenError(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? Description);
}

/// <summary>EVE SSO's signing keys, fetched from its metadata document and refreshed periodically.</summary>
public sealed class EveSigningKeys
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _manager;

    public EveSigningKeys(IHttpClientFactory httpClientFactory, IOptions<HuntersOptions> options)
    {
        var retriever = new HttpDocumentRetriever(httpClientFactory.CreateClient(nameof(EveSigningKeys))) { RequireHttps = true };
        _manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            options.Value.Sso.MetadataUrl, new OpenIdConnectConfigurationRetriever(), retriever);
    }

    public async Task<IEnumerable<SecurityKey>> GetAsync(CancellationToken cancellationToken) =>
        (await _manager.GetConfigurationAsync(cancellationToken)).SigningKeys;
}

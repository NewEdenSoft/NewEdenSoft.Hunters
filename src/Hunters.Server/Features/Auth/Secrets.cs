using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace NewEdenSoft.Hunters.Server.Features.Auth;

/// <summary>Random tokens, hashes and the app-side PKCE-style challenge check.</summary>
public static class Secrets
{
    /// <summary>A URL-safe random token with 256 bits of entropy, optionally prefixed (<c>hs_…</c>).</summary>
    public static string NewToken(string prefix = "") =>
        prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Sha256(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));

    public static string Sha256Base64Url(string value) => WebEncoders.Base64UrlEncode(Sha256(value));

    /// <summary>True when <c>base64url(sha256(verifier)) == challenge</c>, compared in constant time.</summary>
    public static bool VerifyChallenge(string verifier, string challenge) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sha256Base64Url(verifier)),
            Encoding.ASCII.GetBytes(challenge));
}

using System.Net;
using NewEdenSoft.Hunters.Contracts.Auth;

namespace NewEdenSoft.Hunters.Client.Sessions;

/// <summary>
/// A failed call to a Hunters server. <see cref="Code"/> is one of <see cref="AuthErrorCodes"/>,
/// one of the client codes below, or null when the server gave only a message.
/// </summary>
public sealed class HuntersApiException(string? code, string message, HttpStatusCode? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>The server no longer accepts the session (revoked, expired or account deleted).</summary>
    public const string SessionEnded = "session_ended";

    public const string ServerUnreachable = "server_unreachable";

    public string? Code { get; } = code;

    public HttpStatusCode? StatusCode { get; } = statusCode;
}

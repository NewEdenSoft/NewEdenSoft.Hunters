using NewEdenSoft.Hunters.Contracts.Auth;

namespace NewEdenSoft.Hunters.Client.Auth;

/// <summary>A login that did not succeed. <see cref="Code"/> is one of <see cref="AuthErrorCodes"/> or the client codes below.</summary>
public sealed class HuntersLoginException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public const string ServerUnreachable = "server_unreachable";
    public const string UnexpectedResponse = "unexpected_response";

    public string Code { get; } = code;
}

using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace NewEdenSoft.Hunters.Server.Features.Auth;

public enum LoginKind
{
    Web,
    Desktop,
}

/// <summary>
/// What a login is for. Carried through EVE inside the OAuth handler's protected state
/// (<see cref="AuthenticationProperties.Items"/>), so it cannot be read or altered in transit and
/// comes back at the callback together with the correlation check.
/// </summary>
public sealed record LoginRequest(
    LoginKind Kind,
    Guid? AccountId,
    Guid? AccessKeyId,
    string? DeviceName,
    string? LoginId = null,
    int? LoopbackPort = null,
    string? AppChallenge = null,
    string? ReturnUrl = null)
{
    private const string Prefix = "hunters.";

    public AuthenticationProperties ToProperties()
    {
        var properties = new AuthenticationProperties();
        Set(properties, nameof(Kind), Kind.ToString());
        Set(properties, nameof(AccountId), AccountId?.ToString());
        Set(properties, nameof(AccessKeyId), AccessKeyId?.ToString());
        Set(properties, nameof(DeviceName), DeviceName);
        Set(properties, nameof(LoginId), LoginId);
        Set(properties, nameof(LoopbackPort), LoopbackPort?.ToString(CultureInfo.InvariantCulture));
        Set(properties, nameof(AppChallenge), AppChallenge);
        Set(properties, nameof(ReturnUrl), ReturnUrl);
        return properties;
    }

    /// <summary>Reads a request back from the callback's properties; null when they are missing or not ours.</summary>
    public static LoginRequest? FromProperties(AuthenticationProperties? properties)
    {
        if (properties is null || !Enum.TryParse<LoginKind>(Get(properties, nameof(Kind)), out var kind))
            return null;

        return new LoginRequest(
            kind,
            Guid.TryParse(Get(properties, nameof(AccountId)), out var account) ? account : null,
            Guid.TryParse(Get(properties, nameof(AccessKeyId)), out var key) ? key : null,
            Get(properties, nameof(DeviceName)),
            Get(properties, nameof(LoginId)),
            int.TryParse(Get(properties, nameof(LoopbackPort)), CultureInfo.InvariantCulture, out var port) ? port : null,
            Get(properties, nameof(AppChallenge)),
            Get(properties, nameof(ReturnUrl)));
    }

    private static void Set(AuthenticationProperties properties, string key, string? value)
    {
        if (value is not null)
            properties.Items[Prefix + key] = value;
    }

    private static string? Get(AuthenticationProperties properties, string key) =>
        properties.Items.TryGetValue(Prefix + key, out var value) ? value : null;
}

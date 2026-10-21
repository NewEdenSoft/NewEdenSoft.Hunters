using Microsoft.AspNetCore.DataProtection;

namespace NewEdenSoft.Hunters.Server.Infrastructure.Eve;

/// <summary>Encrypts EVE tokens at rest with ASP.NET Data Protection.</summary>
public sealed class TokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("NewEdenSoft.Hunters.EveTokens.v1");

    public string Protect(string token) => _protector.Protect(token);

    /// <summary>Null when the value cannot be decrypted, for example after the keys were lost.</summary>
    public string? Unprotect(string? protectedToken)
    {
        if (protectedToken is null)
            return null;

        try
        {
            return _protector.Unprotect(protectedToken);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

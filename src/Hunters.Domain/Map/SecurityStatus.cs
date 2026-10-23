namespace NewEdenSoft.Hunters.Domain.Map;

/// <summary>Security status as EVE shows it, the same rule the clients use for colours.</summary>
public static class SecurityStatus
{
    /// <summary>
    /// One decimal, halves rounding up; anything above 0.0 and below 0.05 shows as 0.1 (it is still low
    /// security, never null).
    /// </summary>
    public static double Display(double security)
    {
        if (security is > 0 and < 0.05)
            return 0.1;
        var shown = Math.Floor(security * 10 + 0.5) / 10;
        return shown == 0 ? 0 : shown;
    }

    /// <summary>High security: shown as 0.5 or more.</summary>
    public static bool IsHighSec(double security) => Display(security) >= 0.5;
}

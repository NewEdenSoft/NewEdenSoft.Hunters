using System.Globalization;

namespace NewEdenSoft.Hunters.Desktop.Map;

/// <summary>Security status as EVE shows it. Matches <c>web/hunters-web/src/map/security.ts</c>.</summary>
public static class SecurityStatus
{
    /// <summary>Colour bucket for null security; 0–10 are 0.0–1.0.</summary>
    public const int NullBucket = 11;

    public const int BucketCount = 12;

    /// <summary>
    /// One decimal, except that anything above 0.0 and below 0.05 shows as 0.1 (it is still low
    /// security, never null).
    /// </summary>
    public static double Display(double security)
    {
        if (security is > 0 and < 0.05)
            return 0.1;
        // Halves round up, like JavaScript's Math.round, so both clients agree.
        var shown = Math.Floor(security * 10 + 0.5) / 10;
        return shown == 0 ? 0 : shown;
    }

    /// <summary>0–10 for 0.0–1.0, <see cref="NullBucket"/> below 0.0.</summary>
    public static int Bucket(double security)
    {
        var shown = Display(security);
        return shown < 0 ? NullBucket : (int)Math.Round(Math.Min(shown, 1) * 10);
    }

    /// <summary>The security as text, e.g. "0.5" or "-0.3".</summary>
    public static string Format(double security) => Display(security).ToString("0.0", CultureInfo.InvariantCulture);
}

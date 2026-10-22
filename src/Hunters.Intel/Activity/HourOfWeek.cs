namespace NewEdenSoft.Hunters.Intel.Activity;

/// <summary>Hours of the week, UTC: 0 = Monday 00:00–01:00 … 167 = Sunday 23:00–24:00.</summary>
public static class HourOfWeek
{
    public const int Count = 168;

    /// <summary>The hour of the week a time falls in.</summary>
    public static short At(DateTimeOffset time)
    {
        var utc = time.UtcDateTime;
        return (short)(((int)utc.DayOfWeek + 6) % 7 * 24 + utc.Hour);
    }

    /// <summary>
    /// The hour an ESI hour covers. Its window ends at <paramref name="periodEnd"/> (its Last-Modified,
    /// a little after the hour or drifting across it), so the hour it mostly covers is the one 30 minutes earlier.
    /// </summary>
    public static short Covered(DateTimeOffset periodEnd) => At(periodEnd - TimeSpan.FromMinutes(30));

    /// <summary>The hour after the one an ESI hour covers: "now", until the next ESI hour arrives.</summary>
    public static short Following(DateTimeOffset periodEnd) => (short)((Covered(periodEnd) + 1) % Count);

    /// <summary>Hours from <paramref name="from"/> forward to <paramref name="to"/>, wrapping at the end of the week.</summary>
    public static int Ahead(int from, int to) => ((to - from) % Count + Count) % Count;
}

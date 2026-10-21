namespace NewEdenSoft.Hunters.Server.Tests;

/// <summary>Unique EVE character ids, so tests sharing one database never collide.</summary>
public static class TestIds
{
    private static long _next = 2_100_000_000 + Random.Shared.Next(0, 1_000_000) * 100L;

    public static long Character() => Interlocked.Increment(ref _next);
}

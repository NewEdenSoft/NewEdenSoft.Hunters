using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class SecurityStatusTests
{
    [Theory]
    [InlineData(1.0, "1.0", 10)]
    [InlineData(0.946, "0.9", 9)]
    [InlineData(0.45, "0.5", 5)]
    [InlineData(0.44, "0.4", 4)]
    [InlineData(0.03, "0.1", 1)]
    [InlineData(0.0, "0.0", 0)]
    [InlineData(-0.04, "0.0", 0)]
    [InlineData(-0.05, "0.0", 0)]
    [InlineData(-0.06, "-0.1", SecurityStatus.NullBucket)]
    [InlineData(-0.99, "-1.0", SecurityStatus.NullBucket)]
    public void Shows_security_like_eve(double security, string shown, int bucket)
    {
        Assert.Equal(shown, SecurityStatus.Format(security));
        Assert.Equal(bucket, SecurityStatus.Bucket(security));
    }
}

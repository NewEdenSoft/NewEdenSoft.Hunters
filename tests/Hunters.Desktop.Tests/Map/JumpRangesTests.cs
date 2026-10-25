using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class JumpRangesTests
{
    [Fact]
    public void Shows_whole_ranges_plainly_and_others_with_decimals()
    {
        Assert.Equal("8 ly", JumpRanges.FormatLy(8));
        Assert.Equal("7.50 ly", JumpRanges.FormatLy(7.5));
        Assert.Equal("12.3 ly", JumpRanges.FormatLy(12.25));
    }
}

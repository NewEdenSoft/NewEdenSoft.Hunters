using NewEdenSoft.Hunters.Desktop.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Map;

public sealed class SystemInfoTests
{
    [Theory]
    [InlineData(950, "950")]
    [InlineData(12_500, "12.5k")]
    [InlineData(3_200_000, "3.2M")]
    [InlineData(1_050_000_000, "1.05B")]
    [InlineData(250_400_000, "250M")]
    [InlineData(2_000_000, "2M")]
    public void Isk_is_shortened_like_EVE_players_do(double isk, string text) => Assert.Equal(text, SystemInfo.FormatIsk(isk));
}

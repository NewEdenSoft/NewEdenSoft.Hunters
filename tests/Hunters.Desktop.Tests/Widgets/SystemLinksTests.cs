using NewEdenSoft.Hunters.Desktop.Widgets.Map;

namespace NewEdenSoft.Hunters.Desktop.Tests.Widgets;

public sealed class SystemLinksTests
{
    [Fact]
    public void Zkillboard_links_the_system_by_id() =>
        Assert.Equal("https://zkillboard.com/system/30000142/", SystemLinks.Zkillboard(30000142).AbsoluteUri);

    [Theory]
    [InlineData("Tama", "https://evemaps.dotlan.net/system/Tama")]
    [InlineData("New Caldari", "https://evemaps.dotlan.net/system/New_Caldari")]
    [InlineData("1DQ1-A", "https://evemaps.dotlan.net/system/1DQ1-A")]
    public void Dotlan_links_the_system_by_name_with_underscores_for_spaces(string name, string url) =>
        Assert.Equal(url, SystemLinks.Dotlan(name).AbsoluteUri);
}

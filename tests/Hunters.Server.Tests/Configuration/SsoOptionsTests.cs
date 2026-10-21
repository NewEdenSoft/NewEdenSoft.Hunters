using NewEdenSoft.Hunters.Server.Configuration;

namespace NewEdenSoft.Hunters.Server.Tests.Configuration;

public sealed class SsoOptionsTests
{
    [Theory]
    [InlineData("http://localhost:5173/auth/eve/callback", "http://localhost:5173")]
    [InlineData("https://hunters.example.com/auth/eve/callback", "https://hunters.example.com")]
    [InlineData("https://example.com/hunters/auth/eve/callback", "https://example.com/hunters")]
    [InlineData("https://example.com/some/other/callback", null)]
    [InlineData("", null)]
    public void Public_base_url_comes_from_the_callback_url(string callbackUrl, string? expected) =>
        Assert.Equal(expected, new SsoOptions { CallbackUrl = callbackUrl }.PublicBaseUrl);
}

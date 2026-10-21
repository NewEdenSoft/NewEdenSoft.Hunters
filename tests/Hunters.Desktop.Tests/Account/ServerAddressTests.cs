using NewEdenSoft.Hunters.Client;

namespace NewEdenSoft.Hunters.Desktop.Tests.Account;

public sealed class ServerAddressTests
{
    [Theory]
    [InlineData("hunters.example.com", "https://hunters.example.com/")]
    [InlineData("  https://hunters.example.com/  ", "https://hunters.example.com/")]
    [InlineData("https://hunters.example.com/some/page?x=1#top", "https://hunters.example.com/")]
    [InlineData("http://localhost:5173", "http://localhost:5173/")]
    [InlineData("hunters.example.com:8443", "https://hunters.example.com:8443/")]
    [InlineData("localhost:5173", "http://localhost:5173/")]
    [InlineData("127.0.0.1:5259", "http://127.0.0.1:5259/")]
    [InlineData("https://localhost:7001", "https://localhost:7001/")]
    public void Accepts_addresses_users_type(string input, string expected)
    {
        Assert.True(ServerAddress.TryParse(input, out var server));
        Assert.Equal(expected, server.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://hunters.example.com")]
    [InlineData("https://user:pass@hunters.example.com")]
    [InlineData("not a url")]
    public void Rejects_anything_else(string? input) => Assert.False(ServerAddress.TryParse(input, out _));
}

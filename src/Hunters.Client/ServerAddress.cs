namespace NewEdenSoft.Hunters.Client;

/// <summary>Turns what a user types as a server address into a base URL.</summary>
public static class ServerAddress
{
    /// <summary>
    /// Accepts <c>hunters.example.com</c>, <c>https://hunters.example.com/</c> or
    /// <c>localhost:5173</c>. Without a scheme, <c>https</c> is assumed, except for loopback
    /// addresses (local development), which use <c>http</c>. Paths, queries and fragments are dropped.
    /// </summary>
    public static bool TryParse(string? input, out Uri server)
    {
        server = null!;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        if (!text.Contains("://", StringComparison.Ordinal))
            text = (IsLoopback(text) ? "http://" : "https://") + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        server = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
        return true;
    }

    private static bool IsLoopback(string address) =>
        Uri.TryCreate("http://" + address, UriKind.Absolute, out var uri) && uri.IsLoopback;
}

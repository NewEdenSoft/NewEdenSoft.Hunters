namespace NewEdenSoft.Hunters.Client;

/// <summary>Turns what a user types as a server address into a base URL.</summary>
public static class ServerAddress
{
    /// <summary>
    /// Accepts <c>hunters.example.com</c>, <c>https://hunters.example.com/</c> or
    /// <c>http://localhost:5173</c>. Without a scheme, <c>https</c> is assumed. Paths, queries and
    /// fragments are dropped.
    /// </summary>
    public static bool TryParse(string? input, out Uri server)
    {
        server = null!;
        var text = input?.Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;

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
}

using System.Net;

namespace NewEdenSoft.Hunters.Client.Auth;

/// <summary>The page the browser shows when the server hands the login back to the app.</summary>
internal static class LoopbackPages
{
    public static string Render(IReadOnlyDictionary<string, string> query) =>
        query.TryGetValue("error", out _)
            ? Page("Login failed", query.GetValueOrDefault("error_description") ?? "The login failed. Return to Hunters and try again.")
            : Page("Login complete", "You can close this tab and return to Hunters.");

    private static string Page(string title, string message) => $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Hunters: {{WebUtility.HtmlEncode(title)}}</title>
        <style>
          body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #0b0f14; color: #d8e1ea; font: 16px system-ui, sans-serif; }
          main { max-width: 28rem; padding: 2rem; border: 1px solid #2a3542; }
          h1 { margin: 0 0 .75rem; font-size: 1.1rem; letter-spacing: .12em; text-transform: uppercase; color: #8fc7ff; }
          p { margin: 0; line-height: 1.5; }
        </style>
        </head>
        <body><main><h1>{{WebUtility.HtmlEncode(title)}}</h1><p>{{WebUtility.HtmlEncode(message)}}</p></main></body>
        </html>
        """;
}

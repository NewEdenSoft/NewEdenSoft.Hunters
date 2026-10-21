using System.Diagnostics;

namespace NewEdenSoft.Hunters.Client.Auth;

/// <summary>Opens a URL in the user's browser.</summary>
public interface IBrowserLauncher
{
    Task OpenAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>Opens URLs in the default browser of the operating system.</summary>
public sealed class SystemBrowserLauncher : IBrowserLauncher
{
    public Task OpenAsync(Uri url, CancellationToken cancellationToken)
    {
        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException("Only http and https URLs can be opened.", nameof(url));

        var startInfo = OperatingSystem.IsWindows() ? new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }
            : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open", [url.AbsoluteUri])
            : new ProcessStartInfo("xdg-open", [url.AbsoluteUri]);
        using var _ = Process.Start(startInfo);
        return Task.CompletedTask;
    }
}

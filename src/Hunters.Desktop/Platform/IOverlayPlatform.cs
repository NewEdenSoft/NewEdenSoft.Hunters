using Avalonia.Controls;

namespace NewEdenSoft.Hunters.Desktop.Platform;

/// <summary>
/// Operating-system specific behaviour for overlay windows. See docs/desktop-client.md
/// (Platform support) for what each platform can do.
/// </summary>
public interface IOverlayPlatform
{
    /// <summary>Whether <see cref="SetClickThrough"/> has any effect on this platform.</summary>
    bool SupportsClickThrough { get; }

    /// <summary>Applies overlay window styles (for example hiding from Alt+Tab). Call once the window is open.</summary>
    void PrepareOverlay(Window window);

    /// <summary>Lets mouse input pass through the window to whatever is underneath.</summary>
    void SetClickThrough(Window window, bool enabled);
}

/// <summary>Platforms without overlay support yet: overlays work, click-through does nothing.</summary>
public sealed class NoOpOverlayPlatform : IOverlayPlatform
{
    public bool SupportsClickThrough => false;

    public void PrepareOverlay(Window window)
    {
    }

    public void SetClickThrough(Window window, bool enabled)
    {
    }
}

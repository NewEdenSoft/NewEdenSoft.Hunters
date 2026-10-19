using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace NewEdenSoft.Hunters.Desktop.Platform.Windows;

/// <summary>Win32 overlay behaviour: tool-window style and click-through via extended window styles.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsOverlayPlatform : IOverlayPlatform
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExLayered = 0x00080000;
    private const uint LwaAlpha = 0x2;

    public bool SupportsClickThrough => true;

    public void PrepareOverlay(Window window)
    {
        if (Handle(window) is { } hwnd)
            SetExStyle(hwnd, GetExStyle(hwnd) | WsExToolWindow);
    }

    public void SetClickThrough(Window window, bool enabled)
    {
        if (Handle(window) is not { } hwnd)
            return;

        var style = GetExStyle(hwnd);
        if (enabled)
        {
            var wasLayered = (style & WsExLayered) != 0;
            SetExStyle(hwnd, style | WsExLayered | WsExTransparent);

            // A newly layered window is invisible until its layered attributes are set.
            if (!wasLayered)
                SetLayeredWindowAttributes(hwnd, 0, 255, LwaAlpha);
        }
        else
        {
            SetExStyle(hwnd, style & ~WsExTransparent);
        }
    }

    private static nint? Handle(Window window) => window.TryGetPlatformHandle()?.Handle;

    private static long GetExStyle(nint hwnd) =>
        nint.Size == 8 ? GetWindowLongPtrW(hwnd, GwlExStyle) : GetWindowLongW(hwnd, GwlExStyle);

    private static void SetExStyle(nint hwnd, long style)
    {
        if (nint.Size == 8)
            SetWindowLongPtrW(hwnd, GwlExStyle, (nint)style);
        else
            SetWindowLongW(hwnd, GwlExStyle, (int)style);
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(nint hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);
}

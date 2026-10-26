using System.Runtime.InteropServices;

namespace NewEdenSoft.Hunters.Desktop.Platform;

/// <summary>
/// Whether the user asked the system for less motion, like the web's <c>prefers-reduced-motion</c>: on
/// Windows, Settings › Accessibility › Visual effects › Animation effects off. Other platforms: no.
/// </summary>
public static partial class ReducedMotion
{
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public static bool IsPreferred()
    {
        if (!OperatingSystem.IsWindows())
            return false;
        return SystemParametersInfoW(SpiGetClientAreaAnimation, 0, out var animate, 0) && !animate;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SystemParametersInfoW(uint uiAction, uint uiParam, [MarshalAs(UnmanagedType.Bool)] out bool pvParam, uint fWinIni);
}

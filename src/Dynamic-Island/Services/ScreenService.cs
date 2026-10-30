using System.Runtime.InteropServices;
using DynamicIsland.Interop;

namespace DynamicIsland.Services;

internal sealed record ScreenInfo(IntPtr Handle, string Device, NativeMethods.RECT Bounds, bool IsPrimary)
{
    public int Width => Bounds.Right - Bounds.Left;

    public int Height => Bounds.Bottom - Bounds.Top;
}

internal static class ScreenService
{
    private const uint MonitorInfoPrimary = 1;

    public static List<ScreenInfo> GetScreens()
    {
        var screens = new List<ScreenInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (NativeMethods.GetMonitorInfo(handle, ref info))
            {
                screens.Add(new ScreenInfo(handle, info.szDevice, info.rcMonitor, (info.dwFlags & MonitorInfoPrimary) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return screens;
    }

    /// <summary>Renvoie l'écran demandé, ou l'écran principal s'il n'est plus branché.</summary>
    public static ScreenInfo? Resolve(string? device)
    {
        var screens = GetScreens();
        return screens.FirstOrDefault(screen => screen.Device == device)
            ?? screens.FirstOrDefault(screen => screen.IsPrimary)
            ?? screens.FirstOrDefault();
    }
}

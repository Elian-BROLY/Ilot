using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Ilot.Services;

internal sealed record SystemStats(double CpuPercent, double MemoryPercent, double MemoryUsedGb, double MemoryTotalGb, double DownloadBytesPerSecond, double UploadBytesPerSecond);

/// <summary>Charge du PC et débit réseau. Les valeurs sont des écarts entre deux lectures : à appeler à intervalle régulier.</summary>
internal sealed class SystemStatsReader
{
    private const double BytesPerGb = 1024d * 1024 * 1024;

    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private long _lastReceived;
    private long _lastSent;
    private long _lastTick;

    public SystemStats Read()
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        ulong idleNow = idle.Ticks, kernelNow = kernel.Ticks, userNow = user.Ticks;
        var total = kernelNow - _lastKernel + (userNow - _lastUser); // Le temps noyau inclut le temps inoccupé.
        var cpu = _lastTick == 0 || total == 0 ? 0 : (1 - (double)(idleNow - _lastIdle) / total) * 100;
        (_lastIdle, _lastKernel, _lastUser) = (idleNow, kernelNow, userNow);

        var memory = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref memory);
        var usedBytes = memory.ullTotalPhys - memory.ullAvailPhys;

        long received = 0, sent = 0;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus == OperationalStatus.Up && adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            {
                var statistics = adapter.GetIPStatistics();
                received += statistics.BytesReceived;
                sent += statistics.BytesSent;
            }
        }

        var tick = Environment.TickCount64;
        var seconds = (tick - _lastTick) / 1000d;
        // Une carte qui s'éteint fait baisser les totaux : le débit ne doit pas devenir négatif.
        var download = _lastTick == 0 ? 0 : Math.Max(0, received - _lastReceived) / seconds;
        var upload = _lastTick == 0 ? 0 : Math.Max(0, sent - _lastSent) / seconds;
        (_lastReceived, _lastSent, _lastTick) = (received, sent, tick);

        return new SystemStats(Math.Clamp(cpu, 0, 100), memory.dwMemoryLoad, usedBytes / BytesPerGb, memory.ullTotalPhys / BytesPerGb, download, upload);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;

        public readonly ulong Ticks => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}

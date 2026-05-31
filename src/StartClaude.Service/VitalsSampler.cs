using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace StartClaude.Service;

public sealed record VitalsSnapshot(
    string MachineName,
    string Os,
    string DotNetVersion,
    TimeSpan SystemUptime,
    double? CpuPercent,
    long TotalMemoryBytes,
    long UsedMemoryBytes,
    double MemoryPercent,
    long DiskTotalBytes,
    long DiskFreeBytes,
    double DiskUsedPercent);

[SupportedOSPlatform("windows")]
public sealed class VitalsSampler
{
    private readonly object _gate = new();
    private ulong _prevIdle;
    private ulong _prevKernel;
    private ulong _prevUser;
    private bool _havePrev;

    /// <summary>
    /// Samples vitals on demand. CPU% is computed against the previous call - the
    /// first call after startup returns null for CpuPercent. No background work runs
    /// unless this method is called (i.e. unless someone hits /status).
    /// </summary>
    public VitalsSnapshot CurrentSnapshot()
    {
        double? cpuPct = SampleCpu();

        var os = RuntimeInformation.OSDescription;
        var dotnet = RuntimeInformation.FrameworkDescription;
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        var gcInfo = GC.GetGCMemoryInfo();
        long total = gcInfo.TotalAvailableMemoryBytes;
        long used = gcInfo.MemoryLoadBytes;
        double memPct = total > 0 ? (double)used / total * 100.0 : 0.0;

        long diskTotal = 0, diskFree = 0;
        try
        {
            var drive = new DriveInfo("C");
            if (drive.IsReady)
            {
                diskTotal = drive.TotalSize;
                diskFree = drive.AvailableFreeSpace;
            }
        }
        catch { }
        double diskUsedPct = diskTotal > 0 ? (1.0 - (double)diskFree / diskTotal) * 100.0 : 0.0;

        return new VitalsSnapshot(
            MachineName: Environment.MachineName,
            Os: os,
            DotNetVersion: dotnet,
            SystemUptime: uptime,
            CpuPercent: cpuPct,
            TotalMemoryBytes: total,
            UsedMemoryBytes: used,
            MemoryPercent: memPct,
            DiskTotalBytes: diskTotal,
            DiskFreeBytes: diskFree,
            DiskUsedPercent: diskUsedPct);
    }

    private double? SampleCpu()
    {
        if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
        {
            return null;
        }
        ulong idle = ((ulong)idleFt.dwHighDateTime << 32) | (uint)idleFt.dwLowDateTime;
        ulong kernel = ((ulong)kernelFt.dwHighDateTime << 32) | (uint)kernelFt.dwLowDateTime;
        ulong user = ((ulong)userFt.dwHighDateTime << 32) | (uint)userFt.dwLowDateTime;

        lock (_gate)
        {
            double? cpuPercent = null;
            if (_havePrev)
            {
                ulong idleDelta = idle - _prevIdle;
                ulong kernelDelta = kernel - _prevKernel;
                ulong userDelta = user - _prevUser;
                ulong totalSys = kernelDelta + userDelta;
                if (totalSys > 0)
                {
                    cpuPercent = (1.0 - (double)idleDelta / totalSys) * 100.0;
                }
            }
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _havePrev = true;
            return cpuPercent;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public int dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);
}

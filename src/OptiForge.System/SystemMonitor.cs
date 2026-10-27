using OptiForge.Core.Models;

namespace OptiForge.SysMon;

/// <summary>
/// Monitor using Win32 GetSystemTimes + GlobalMemoryStatusEx + DriveInfo (no NuGet).
/// </summary>
public sealed class SystemMonitor : ISystemMonitor
{
    public SystemSnapshot GetSnapshot()
    {
        var cpu = NativeCpu.NextValue();
        NativeMemory.TryGet(out var ramPct, out var ramUsed, out var ramTotal);
        var disk = GetSystemDisk();

        return new SystemSnapshot
        {
            CpuPercent = Math.Clamp(cpu, 0, 100),
            RamUsedPercent = ramPct,
            RamUsedGb = ramUsed,
            RamTotalGb = ramTotal,
            DiskFreeGb = disk.freeGb,
            DiskTotalGb = disk.totalGb,
            SystemDrive = disk.drive,
            Timestamp = DateTimeOffset.Now
        };
    }

    private static (string drive, double freeGb, double totalGb) GetSystemDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var di = new DriveInfo(root);
            if (!di.IsReady)
                return (root.TrimEnd('\\'), 0, 0);

            return (
                di.Name.TrimEnd('\\'),
                di.AvailableFreeSpace / (1024.0 * 1024 * 1024),
                di.TotalSize / (1024.0 * 1024 * 1024));
        }
        catch
        {
            return ("C:", 0, 0);
        }
    }

    public void Dispose()
    {
        // nothing to dispose
    }
}

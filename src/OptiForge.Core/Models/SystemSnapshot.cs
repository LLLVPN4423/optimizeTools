namespace OptiForge.Core.Models;

public sealed class SystemSnapshot
{
    public double CpuPercent { get; init; }
    public double RamUsedPercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public double DiskFreeGb { get; init; }
    public double DiskTotalGb { get; init; }
    public string SystemDrive { get; init; } = "C:";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

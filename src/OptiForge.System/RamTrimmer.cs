using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace OptiForge.SysMon;

public sealed class RamTrimReport
{
    public bool IsAdministrator { get; init; }
    public int ProcessesTouched { get; set; }
    public int ProcessesFailed { get; set; }
    public bool StandbyCleared { get; set; }
    public string? StandbyNote { get; set; }
    public double RamPercentBefore { get; init; }
    public double RamPercentAfter { get; set; }
    public double RamUsedGbBefore { get; init; }
    public double RamUsedGbAfter { get; set; }
    public string Summary { get; set; } = "";
}

/// <summary>
/// Trims process working sets; optionally clears standby list when elevated.
/// Inspired by documented Windows APIs used by open-source WinMemoryCleaner.
/// </summary>
public sealed class RamTrimmer
{
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public RamTrimReport Trim(bool clearStandbyList = true)
    {
        NativeMemory.TryGet(out var beforePct, out var beforeUsed, out _);
        var report = new RamTrimReport
        {
            IsAdministrator = IsRunningAsAdministrator(),
            RamPercentBefore = beforePct,
            RamUsedGbBefore = beforeUsed
        };

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                using (proc)
                {
                    if (EmptyWorkingSet(proc.Handle))
                        report.ProcessesTouched++;
                    else
                        report.ProcessesFailed++;
                }
            }
            catch
            {
                report.ProcessesFailed++;
                try { proc.Dispose(); } catch { /* ignore */ }
            }
        }

        if (clearStandbyList)
        {
            if (!report.IsAdministrator)
            {
                report.StandbyNote = "Standby list cần Run as Administrator — đã bỏ qua.";
            }
            else
            {
                try
                {
                    report.StandbyCleared = TryClearStandbyList(out var note);
                    report.StandbyNote = note;
                }
                catch (Exception ex)
                {
                    report.StandbyNote = ex.Message;
                }
            }
        }

        // Give OS a moment to update memory counters
        Thread.Sleep(300);
        NativeMemory.TryGet(out var afterPct, out var afterUsed, out _);
        report.RamPercentAfter = afterPct;
        report.RamUsedGbAfter = afterUsed;

        var freed = Math.Max(0, beforeUsed - afterUsed);
        report.Summary =
            $"Trimmed {report.ProcessesTouched} processes" +
            (report.ProcessesFailed > 0 ? $" ({report.ProcessesFailed} skipped)" : "") +
            $". RAM {beforePct:0}% → {afterPct:0}% (≈ {freed:0.00} GB shown free)." +
            (string.IsNullOrWhiteSpace(report.StandbyNote) ? "" : " " + report.StandbyNote);

        return report;
    }

    [DllImport("psapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    // SYSTEM_MEMORY_LIST_COMMAND.MemoryPurgeStandbyList = 4
    private const int MemoryPurgeStandbyList = 4;
    private const int SystemMemoryListInformation = 80;

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    private static bool TryClearStandbyList(out string note)
    {
        var cmd = MemoryPurgeStandbyList;
        var status = NtSetSystemInformation(SystemMemoryListInformation, ref cmd, sizeof(int));
        if (status == 0)
        {
            note = "Standby list cleared.";
            return true;
        }

        note = $"Standby clear failed (NTSTATUS 0x{status:X8}). Working-set trim vẫn đã chạy.";
        return false;
    }
}

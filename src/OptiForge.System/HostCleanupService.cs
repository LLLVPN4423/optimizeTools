using System.Diagnostics;
using OptiForge.Core;
using OptiForge.Core.Models;

namespace OptiForge.SysMon;

/// <summary>
/// Mitigates host-machine side effects when OptiForge runs from a removable drive.
/// </summary>
public sealed class HostCleanupService
{
    private readonly PortablePaths _paths;
    private readonly NetworkThrottle _net = new();
    private readonly StartupManager _startup = new();

    public HostCleanupService(PortablePaths paths) => _paths = paths;

    /// <summary>
    /// Full host cleanup before eject: stop our llama processes, remove QoS, optionally restore startups.
    /// </summary>
    public HostImpactReport CleanupForEject(
        bool removeQos = true,
        bool restoreStartups = false,
        bool clearPortableTemp = true,
        bool killLlamaFromOurRuntime = true)
    {
        var report = new HostImpactReport();

        if (killLlamaFromOurRuntime)
        {
            var killed = KillOurLlamaProcesses(report);
            report.LlamaStopped = killed > 0;
            if (killed > 0)
                report.Actions.Add($"Stopped {killed} llama process(es) started from this drive.");
            else
                report.Actions.Add("No OptiForge llama process left running.");
        }

        if (removeQos)
        {
            var (n, msg) = _net.RemoveAllOptiForgePolicies();
            report.QosPoliciesRemoved = n;
            report.Actions.Add(msg);
            if (n > 0 && !RamTrimmer.IsRunningAsAdministrator())
                report.Warnings.Add("QoS removal may need Administrator if policies remain.");
        }

        if (restoreStartups)
        {
            var (n, msg) = _startup.RestoreAllFromBackup(dryRun: false);
            report.StartupEntriesRestored = n;
            report.Actions.Add(msg);
            _startup.TryRemoveEmptyOptiForgeKey();
        }

        if (clearPortableTemp)
        {
            report.TempFilesCleared = ClearPortableTempFiles();
            if (report.TempFilesCleared > 0)
                report.Actions.Add($"Cleared {report.TempFilesCleared} temp file(s) under App/data (zips/partials).");
        }

        report.Summary = BuildSummary(report);
        return report;
    }

    public HostImpactReport AnalyzeCurrentImpact()
    {
        var report = new HostImpactReport();

        var policies = _net.ListOptiForgePolicies();
        if (policies.Count > 0)
            report.Warnings.Add($"Host has {policies.Count} OptiForge QoS policy(ies): {string.Join(", ", policies)}");
        else
            report.Actions.Add("No OptiForge QoS policies on host.");

        var disabled = _startup.ListUserStartup().Count(e => !e.Enabled);
        if (disabled > 0)
            report.Warnings.Add($"{disabled} startup entr(y/ies) disabled via OptiForge backup key (persist until restored).");
        else
            report.Actions.Add("No OptiForge startup backups pending.");

        var llama = CountOurLlamaProcesses();
        if (llama > 0)
            report.Warnings.Add($"{llama} llama-server process(es) from this Runtime still running (close app / Safe eject).");
        else
            report.Actions.Add("No orphan llama-server from this drive.");

        report.Summary = report.Warnings.Count == 0
            ? "Host looks clean of OptiForge side effects."
            : $"Found {report.Warnings.Count} host impact item(s). Use Safe eject / Cleanup.";
        return report;
    }

    private int KillOurLlamaProcesses(HostImpactReport report)
    {
        var roots = new[]
        {
            Path.GetFullPath(_paths.LlamaDir),
            Path.GetFullPath(_paths.Root)
        };

        var killed = 0;
        var seen = new HashSet<int>();

        foreach (var name in new[] { "llama-server", "llama-cli" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                if (!seen.Add(p.Id))
                {
                    try { p.Dispose(); } catch { }
                    continue;
                }

                try
                {
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch { /* access denied */ }

                    if (path is null)
                        continue;

                    var full = Path.GetFullPath(path);
                    var ours = roots.Any(r => full.StartsWith(r, StringComparison.OrdinalIgnoreCase));
                    if (!ours)
                        continue;

                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                    killed++;
                }
                catch (Exception ex)
                {
                    report.Warnings.Add($"Could not stop PID {p.Id}: {ex.Message}");
                }
                finally
                {
                    try { p.Dispose(); } catch { /* ignore */ }
                }
            }
        }

        return killed;
    }

    private int CountOurLlamaProcesses()
    {
        var root = Path.GetFullPath(_paths.LlamaDir);
        var n = 0;
        foreach (var p in Process.GetProcessesByName("llama-server"))
        {
            try
            {
                var path = p.MainModule?.FileName;
                if (path is not null &&
                    Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    n++;
            }
            catch { /* ignore */ }
            finally { try { p.Dispose(); } catch { } }
        }
        return n;
    }

    private int ClearPortableTempFiles()
    {
        var n = 0;
        var data = _paths.DataDir;
        if (!Directory.Exists(data)) return 0;

        foreach (var pattern in new[] { "*.zip", "*.partial", "apply-update.bat" })
        {
            foreach (var f in Directory.EnumerateFiles(data, pattern))
            {
                try { File.Delete(f); n++; } catch { /* locked */ }
            }
        }

        var extract = Path.Combine(data, "update-extract");
        if (Directory.Exists(extract))
        {
            try { Directory.Delete(extract, true); n++; } catch { /* ignore */ }
        }

        return n;
    }

    private static string BuildSummary(HostImpactReport r)
    {
        return $"Cleanup: llama={(r.LlamaStopped ? "stopped" : "n/a")}, QoS removed={r.QosPoliciesRemoved}, " +
               $"startups restored={r.StartupEntriesRestored}, temp cleared={r.TempFilesCleared}. " +
               (r.Warnings.Count > 0 ? $"Warnings: {r.Warnings.Count}." : "OK for eject.");
    }
}

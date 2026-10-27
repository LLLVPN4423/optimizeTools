using System.Diagnostics;
using System.Text;

namespace OptiForge.SysMon;

public sealed class NetworkThrottleRule
{
    public string ProcessName { get; set; } = ""; // e.g. chrome.exe
    public int Kbps { get; set; } // 0 = unlimited / remove
}

public sealed class NetworkThrottleResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public bool NeedsAdmin { get; init; }
}

/// <summary>
/// Uses Windows QoS policies (New-NetQosPolicy) — no WinDivert driver.
/// Requires Administrator. Soft limit suitable for keeping calls usable while AI/downloads run.
/// </summary>
public sealed class NetworkThrottle
{
    public static string PolicyName(string processName)
        => "OptiForge_" + Path.GetFileNameWithoutExtension(processName).Replace(" ", "_");

    public NetworkThrottleResult Apply(NetworkThrottleRule rule)
    {
        if (!RamTrimmer.IsRunningAsAdministrator())
        {
            return new NetworkThrottleResult
            {
                Ok = false,
                NeedsAdmin = true,
                Message = "Giới hạn mạng cần Run as Administrator (Windows QoS)."
            };
        }

        var exe = rule.ProcessName.Trim();
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            exe += ".exe";

        var name = PolicyName(exe);
        try
        {
            // Remove existing policy with same name (ignore errors)
            RunPs($"Remove-NetQosPolicy -Name '{name}' -Confirm:$false -ErrorAction SilentlyContinue");

            if (rule.Kbps <= 0)
            {
                return new NetworkThrottleResult
                {
                    Ok = true,
                    Message = $"Đã gỡ giới hạn cho {exe}."
                };
            }

            // ThrottleRateActionBitsPerSecond
            var bits = (ulong)rule.Kbps * 1000L;
            var script =
                $"New-NetQosPolicy -Name '{name}' -AppPathNameMatchCondition '{exe}' " +
                $"-IPProtocolMatchCondition Both -ThrottleRateActionBitsPerSecond {bits} -ErrorAction Stop";
            var (code, output) = RunPs(script);
            if (code != 0)
            {
                return new NetworkThrottleResult
                {
                    Ok = false,
                    Message = "QoS lỗi: " + output
                };
            }

            return new NetworkThrottleResult
            {
                Ok = true,
                Message = $"Đã giới hạn {exe} ≈ {rule.Kbps} KB/s (QoS)."
            };
        }
        catch (Exception ex)
        {
            return new NetworkThrottleResult { Ok = false, Message = ex.Message };
        }
    }

    public IReadOnlyList<string> ListOptiForgePolicies()
    {
        var (code, output) = RunPs(
            "Get-NetQosPolicy | Where-Object { $_.Name -like 'OptiForge_*' } | Select-Object -ExpandProperty Name");
        if (code != 0 || string.IsNullOrWhiteSpace(output))
            return [];
        return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Removes every OptiForge_* QoS policy from the host.</summary>
    public (int removed, string message) RemoveAllOptiForgePolicies()
    {
        var names = ListOptiForgePolicies();
        if (names.Count == 0)
            return (0, "No OptiForge QoS policies to remove.");

        if (!RamTrimmer.IsRunningAsAdministrator())
        {
            // Still try — some policies may be removable without elevation depending on how created
        }

        var removed = 0;
        foreach (var name in names)
        {
            var safe = name.Replace("'", "''");
            var (code, _) = RunPs($"Remove-NetQosPolicy -Name '{safe}' -Confirm:$false -ErrorAction SilentlyContinue");
            if (code == 0) removed++;
        }

        var left = ListOptiForgePolicies();
        if (left.Count > 0)
        {
            return (removed,
                $"Removed {removed} QoS policy(ies); {left.Count} remain — run OptiForge as Administrator to finish.");
        }

        return (removed, $"Removed {removed} OptiForge QoS policy(ies) from host.");
    }

    private static (int exitCode, string output) RunPs(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " + EscapeArg(command),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi)!;
        var sb = new StringBuilder();
        sb.Append(p.StandardOutput.ReadToEnd());
        sb.Append(p.StandardError.ReadToEnd());
        p.WaitForExit(60_000);
        return (p.ExitCode, sb.ToString().Trim());
    }

    private static string EscapeArg(string cmd)
        => "\"" + cmd.Replace("\"", "\\\"") + "\"";
}

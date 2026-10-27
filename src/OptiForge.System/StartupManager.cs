using Microsoft.Win32;

namespace OptiForge.SysMon;

public sealed class StartupEntry
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Hive { get; init; } = "HKCU"; // HKCU | HKLM
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Safe "debloat-lite": manage current-user Run startup entries with backup key.
/// No aggressive registry cleaner / service nuking.
/// </summary>
public sealed class StartupManager
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string BackupPath = @"Software\OptiForge\StartupDisabled";

    public IReadOnlyList<StartupEntry> ListUserStartup()
    {
        var list = new List<StartupEntry>();
        list.AddRange(ReadRunKey(Registry.CurrentUser, RunPath, "HKCU", enabled: true));
        list.AddRange(ReadRunKey(Registry.CurrentUser, BackupPath, "HKCU", enabled: false));
        return list;
    }

    public (bool ok, string message) Disable(string name, bool dryRun)
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
            if (run is null) return (false, "Không mở được HKCU Run.");
            var value = run.GetValue(name)?.ToString();
            if (value is null) return (false, $"Không thấy '{name}' trong Run.");

            if (dryRun)
                return (true, $"[Dry-run] Sẽ tắt startup '{name}'.");

            using var backup = Registry.CurrentUser.CreateSubKey(BackupPath);
            backup?.SetValue(name, value);
            run.DeleteValue(name, throwOnMissingValue: false);
            return (true, $"Đã tắt startup '{name}' (có thể bật lại).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public (bool ok, string message) Enable(string name, bool dryRun)
    {
        try
        {
            using var backup = Registry.CurrentUser.OpenSubKey(BackupPath, writable: true);
            if (backup is null) return (false, "Không có backup startup.");
            var value = backup.GetValue(name)?.ToString();
            if (value is null) return (false, $"Không thấy '{name}' trong backup.");

            if (dryRun)
                return (true, $"[Dry-run] Sẽ bật lại startup '{name}'.");

            using var run = Registry.CurrentUser.CreateSubKey(RunPath);
            run?.SetValue(name, value);
            backup.DeleteValue(name, throwOnMissingValue: false);
            return (true, $"Đã bật lại startup '{name}'.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static IEnumerable<StartupEntry> ReadRunKey(RegistryKey root, string path, string hive, bool enabled)
    {
        using var key = root.OpenSubKey(path, writable: false);
        if (key is null) yield break;
        foreach (var name in key.GetValueNames())
        {
            yield return new StartupEntry
            {
                Name = name,
                Command = key.GetValue(name)?.ToString() ?? "",
                Hive = hive,
                Enabled = enabled
            };
        }
    }
}

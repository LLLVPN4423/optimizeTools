using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptiForge.Core;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public bool StartMinimizedToTray { get; set; }
    public bool EnableAiOnLaunch { get; set; }
    public int AiPort { get; set; } = 8081;
    public string PreferredModelTier { get; set; } = "fast"; // fast | quality | auto
    public bool DryRunCleanByDefault { get; set; } = true;
    public int MonitorIntervalMs { get; set; } = 1000;
    /// <summary>On exit: stop llama from this drive + remove OptiForge QoS policies.</summary>
    public bool AutoCleanupHostOnExit { get; set; } = true;
    /// <summary>On exit: also restore startups disabled via OptiForge (default off — user intent).</summary>
    public bool RestoreStartupsOnExit { get; set; } = false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static AppSettings Load(PortablePaths paths)
    {
        try
        {
            if (File.Exists(paths.SettingsPath))
            {
                var json = File.ReadAllText(paths.SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt settings → defaults
        }

        var defaults = new AppSettings();
        defaults.Save(paths);
        return defaults;
    }

    public void Save(PortablePaths paths)
    {
        Directory.CreateDirectory(paths.DataDir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(paths.SettingsPath, json);
    }
}

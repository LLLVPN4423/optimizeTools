namespace OptiForge.Core;

/// <summary>
/// Resolves all paths relative to the portable root (folder that contains Launch.bat / App / Models).
/// Works when the drive letter changes (E: → F:).
/// </summary>
public sealed class PortablePaths
{
    public string Root { get; }
    public string AppDir { get; }
    public string DataDir { get; }
    public string ModelsDir { get; }
    public string ModelsFastDir { get; }
    public string ModelsQualityDir { get; }
    public string LlamaDir { get; }
    public string KnowledgeDir { get; }
    public string SettingsPath { get; }
    public string LogDir { get; }

    public PortablePaths(string? rootOverride = null)
    {
        Root = ResolveRoot(rootOverride);
        AppDir = Path.Combine(Root, "App");
        DataDir = Path.Combine(AppDir, "data");
        ModelsDir = Path.Combine(Root, "Models");
        ModelsFastDir = Path.Combine(ModelsDir, "fast");
        ModelsQualityDir = Path.Combine(ModelsDir, "quality");
        LlamaDir = Path.Combine(Root, "Runtime", "llama");
        KnowledgeDir = Path.Combine(Root, "Knowledge");
        SettingsPath = Path.Combine(DataDir, "settings.json");
        LogDir = Path.Combine(DataDir, "logs");

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(ModelsFastDir);
        Directory.CreateDirectory(ModelsQualityDir);
        Directory.CreateDirectory(KnowledgeDir);
    }

    private static string ResolveRoot(string? rootOverride)
    {
        var fromEnv = Environment.GetEnvironmentVariable("OPTIFORGE_ROOT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return Path.GetFullPath(fromEnv.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (!string.IsNullOrWhiteSpace(rootOverride) && Directory.Exists(rootOverride))
            return Path.GetFullPath(rootOverride);

        // Dev: bin/Debug/netX/ → walk up until we find Models or Launch.bat
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            var launch = Path.Combine(dir.FullName, "Launch.bat");
            var models = Path.Combine(dir.FullName, "Models");
            if (File.Exists(launch) || Directory.Exists(models))
                return dir.FullName;
        }

        // Fallback: treat parent of App as root when published into App\
        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        if (baseDir.Name.Equals("App", StringComparison.OrdinalIgnoreCase) && baseDir.Parent is not null)
            return baseDir.Parent.FullName;

        return baseDir.FullName;
    }
}

using OptiForge.Core.Models;

namespace OptiForge.SysMon;

/// <summary>
/// Safe disk cleaner: temp folders + optional recycle bin.
/// Always supports dry-run. Skips locked files without failing the whole run.
/// </summary>
public sealed class DiskCleaner
{
    public IReadOnlyList<CleanCategoryDef> Categories { get; } =
    [
        new()
        {
            Id = "user-temp",
            DisplayName = "User Temp",
            Description = "%TEMP% — temporary files older than 1 day",
            SelectedByDefault = true,
            MinAgeDays = 1
        },
        new()
        {
            Id = "localapp-temp",
            DisplayName = "LocalAppData Temp",
            Description = "%LOCALAPPDATA%\\Temp",
            SelectedByDefault = true,
            MinAgeDays = 1
        },
        new()
        {
            Id = "windows-temp",
            DisplayName = "Windows Temp",
            Description = "%WINDIR%\\Temp (may need admin for some files)",
            SelectedByDefault = true,
            MinAgeDays = 1
        },
        new()
        {
            Id = "thumbcache",
            DisplayName = "Thumbnail cache",
            Description = "Explorer thumbcache_*.db (skip if locked)",
            SelectedByDefault = false,
            MinAgeDays = 0
        },
        new()
        {
            Id = "recycle-bin",
            DisplayName = "Recycle Bin",
            Description = "Empty Recycle Bin for all drives",
            SelectedByDefault = false,
            MinAgeDays = 0
        }
    ];

    public CleanReport Scan(IEnumerable<string> selectedIds, CancellationToken ct = default)
        => Run(selectedIds, dryRun: true, ct);

    public CleanReport Clean(IEnumerable<string> selectedIds, bool dryRun, CancellationToken ct = default)
        => Run(selectedIds, dryRun, ct);

    private CleanReport Run(IEnumerable<string> selectedIds, bool dryRun, CancellationToken ct)
    {
        var selected = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
        var report = new CleanReport { DryRun = dryRun, StartedAt = DateTimeOffset.Now };

        foreach (var def in Categories)
        {
            ct.ThrowIfCancellationRequested();
            var result = new CleanCategoryResult
            {
                Id = def.Id,
                DisplayName = def.DisplayName,
                Selected = selected.Contains(def.Id)
            };

            if (!result.Selected)
            {
                report.Categories.Add(result);
                continue;
            }

            try
            {
                switch (def.Id)
                {
                    case "user-temp":
                        ProcessFolder(Path.GetTempPath(), def.MinAgeDays, dryRun, result, ct);
                        break;
                    case "localapp-temp":
                        ProcessFolder(
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
                            def.MinAgeDays, dryRun, result, ct);
                        break;
                    case "windows-temp":
                        ProcessFolder(
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"),
                            def.MinAgeDays, dryRun, result, ct);
                        break;
                    case "thumbcache":
                        ProcessThumbcache(dryRun, result, ct);
                        break;
                    case "recycle-bin":
                        ProcessRecycleBin(dryRun, result);
                        break;
                }
            }
            catch (Exception ex)
            {
                result.Note = ex.Message;
            }

            report.Categories.Add(result);
        }

        report.FinishedAt = DateTimeOffset.Now;
        var foundMb = report.TotalBytesFound / (1024.0 * 1024);
        var deletedMb = report.TotalBytesDeleted / (1024.0 * 1024);
        report.Summary = dryRun
            ? $"Dry-run: ~{foundMb:0.0} MB có thể thu hồi ({report.Categories.Where(c => c.Selected).Sum(c => c.FileCount)} files)."
            : $"Cleaned: ~{deletedMb:0.0} MB deleted. Failed files: {report.Categories.Sum(c => c.FilesFailed)}.";
        return report;
    }

    private static void ProcessFolder(string root, int minAgeDays, bool dryRun, CleanCategoryResult result, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            result.Note = "Folder not found";
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(0, minAgeDays));
        foreach (var file in EnumerateFilesSafe(root))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists) continue;
                if (minAgeDays > 0 && info.LastWriteTimeUtc > cutoff) continue;

                result.FileCount++;
                result.BytesFound += info.Length;

                if (dryRun) continue;

                try
                {
                    info.Attributes &= ~(FileAttributes.ReadOnly | FileAttributes.Hidden);
                    info.Delete();
                    result.FilesDeleted++;
                    result.BytesDeleted += info.Length;
                }
                catch
                {
                    result.FilesFailed++;
                }
            }
            catch
            {
                // skip unreadable
            }
        }

        // Remove empty directories (best-effort, bottom-up)
        if (!dryRun)
            TryRemoveEmptyDirs(root);
    }

    private static void ProcessThumbcache(bool dryRun, CleanCategoryResult result, CancellationToken ct)
    {
        var explorer = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Explorer");
        if (!Directory.Exists(explorer))
        {
            result.Note = "Explorer cache folder not found";
            return;
        }

        foreach (var file in Directory.EnumerateFiles(explorer, "thumbcache_*.db"))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(file);
                result.FileCount++;
                result.BytesFound += info.Length;
                if (dryRun) continue;
                try
                {
                    info.Delete();
                    result.FilesDeleted++;
                    result.BytesDeleted += info.Length;
                }
                catch
                {
                    result.FilesFailed++;
                }
            }
            catch
            {
                // skip
            }
        }
    }

    private static void ProcessRecycleBin(bool dryRun, CleanCategoryResult result)
    {
        // Estimate size is expensive; for MVP we just report action.
        result.Note = dryRun
            ? "Dry-run: will call SHEmptyRecycleBin (all drives)"
            : null;

        if (dryRun)
        {
            result.FileCount = 1;
            return;
        }

        try
        {
            // SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND
            const int flags = 0x1 | 0x2 | 0x4;
            var hr = NativeShell.SHEmptyRecycleBin(IntPtr.Zero, null, flags);
            if (hr != 0 && hr != -2147418113) // ignore some benign HRESULTs
                result.Note = $"SHEmptyRecycleBin HRESULT=0x{hr:X8}";
            else
            {
                result.FilesDeleted = 1;
                result.Note = "Recycle Bin emptied";
            }
        }
        catch (Exception ex)
        {
            result.FilesFailed = 1;
            result.Note = ex.Message;
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            IEnumerable<string> files = Array.Empty<string>();
            IEnumerable<string> dirs = Array.Empty<string>();
            try { files = Directory.EnumerateFiles(dir); } catch { /* skip */ }
            try { dirs = Directory.EnumerateDirectories(dir); } catch { /* skip */ }

            foreach (var f in files)
                yield return f;
            foreach (var d in dirs)
                stack.Push(d);
        }
    }

    private static void TryRemoveEmptyDirs(string root)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir, false);
                }
                catch { /* skip */ }
            }
        }
        catch { /* skip */ }
    }
}

internal static class NativeShell
{
    [System.Runtime.InteropServices.DllImport("Shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    public static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, int dwFlags);
}

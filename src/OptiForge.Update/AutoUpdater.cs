using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using OptiForge.Core;

namespace OptiForge.Update;

public sealed class UpdateResult
{
    public bool UpdateAvailable { get; init; }
    public bool Applied { get; init; }
    public string Message { get; init; } = "";
    public string? Version { get; init; }
}

/// <summary>
/// Downloads OptiForge-win-*.zip from GitHub Releases and schedules replace-on-restart.
/// Full Velopack packaging can replace this later; this works offline-friendly after download.
/// </summary>
public sealed class AutoUpdater
{
    private readonly PortablePaths _paths;
    public string RepositoryUrl { get; } = "https://github.com/LLLVPN4423/optimizeTools";
    private const string ApiLatest = "https://api.github.com/repos/LLLVPN4423/optimizeTools/releases/latest";

    public string CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";

    public AutoUpdater(PortablePaths paths) => _paths = paths;

    public async Task<UpdateResult> CheckAndDownloadAsync(
        bool applyAndRestart,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OptiForge", CurrentVersion));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        progress?.Report("Checking GitHub Releases…");
        using var resp = await http.GetAsync(ApiLatest, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new UpdateResult
            {
                Message = $"Chưa có Release. Tạo release kèm asset OptiForge-win-x64.zip. Local={CurrentVersion}"
            };
        }

        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
        if (string.IsNullOrWhiteSpace(tag))
            return new UpdateResult { Message = "Release thiếu tag_name." };

        if (!IsNewer(tag, CurrentVersion))
        {
            return new UpdateResult
            {
                UpdateAvailable = false,
                Version = tag,
                Message = $"Đã mới nhất (local {CurrentVersion}, remote {tag})."
            };
        }

        string? zipUrl = null;
        string? zipName = null;
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var lower = name.ToLowerInvariant();
            if (lower.EndsWith(".zip") && lower.Contains("optiforge") && lower.Contains("win"))
            {
                zipUrl = asset.GetProperty("browser_download_url").GetString();
                zipName = name;
                break;
            }
        }

        if (zipUrl is null)
        {
            return new UpdateResult
            {
                UpdateAvailable = true,
                Version = tag,
                Message = $"Có bản {tag} nhưng chưa có asset OptiForge-win-*.zip để auto-update."
            };
        }

        var zipPath = Path.Combine(_paths.DataDir, zipName ?? "update.zip");
        progress?.Report($"Downloading {tag}…");
        await using (var input = await http.GetStreamAsync(zipUrl, ct))
        await using (var output = File.Create(zipPath))
            await input.CopyToAsync(output, ct);

        var extractDir = Path.Combine(_paths.DataDir, "update-extract");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        ZipFile.ExtractToDirectory(zipPath, extractDir, true);

        if (!applyAndRestart)
        {
            return new UpdateResult
            {
                UpdateAvailable = true,
                Version = tag,
                Message = $"Đã tải {tag} vào {extractDir}. Bật Apply để cài và restart."
            };
        }

        var script = Path.Combine(_paths.DataDir, "apply-update.bat");
        var pid = Environment.ProcessId;
        var appDir = _paths.AppDir;
        var exe = Path.Combine(appDir, "OptiForge.exe");
        if (!File.Exists(exe)) exe = Path.Combine(appDir, "OptiForge.App.exe");

        File.WriteAllText(script, $"""
            @echo off
            echo Waiting for OptiForge to exit...
            :wait
            tasklist /FI "PID eq {pid}" 2>NUL | find "{pid}" >NUL
            if "%ERRORLEVEL%"=="0" (
              timeout /t 1 /nobreak >NUL
              goto wait
            )
            echo Copying update...
            xcopy /E /Y /I "{extractDir}\*" "{appDir}\"
            if exist "{extractDir}\App\*" xcopy /E /Y /I "{extractDir}\App\*" "{appDir}\"
            start "" "{exe}"
            del "%~f0"
            """);

        progress?.Report("Restarting to apply update…");
        Process.Start(new ProcessStartInfo
        {
            FileName = script,
            UseShellExecute = true,
            CreateNoWindow = true
        });

        return new UpdateResult
        {
            UpdateAvailable = true,
            Applied = true,
            Version = tag,
            Message = $"Đang áp dụng {tag} và restart…"
        };
    }

    private static bool IsNewer(string remote, string local)
    {
        if (!Version.TryParse(Pad(remote), out var r)) return false;
        if (!Version.TryParse(Pad(local), out var l)) return true;
        return r > l;
    }

    private static string Pad(string v)
    {
        v = v.Trim().TrimStart('v', 'V');
        var p = v.Split('.');
        return p.Length switch
        {
            1 => v + ".0.0",
            2 => v + ".0",
            _ => $"{p[0]}.{p[1]}.{p[2]}"
        };
    }
}

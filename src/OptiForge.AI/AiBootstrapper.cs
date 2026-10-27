using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using OptiForge.Core;

namespace OptiForge.AI;

public sealed class BootstrapProgress
{
    public string Stage { get; init; } = "";
    public double Percent { get; init; }
    public string Detail { get; init; } = "";
}

/// <summary>
/// Downloads llama-server (llama.cpp GitHub release) and a small GGUF into the portable folders.
/// Models are NOT committed to git — stored on the drive only.
/// </summary>
public sealed class AiBootstrapper
{
    private readonly PortablePaths _paths;
    private readonly HttpClient _http;

    // Stable HF mirror for ~2GB Q4 model (fast tier)
    public const string DefaultModelUrl =
        "https://huggingface.co/Qwen/Qwen2.5-3B-Instruct-GGUF/resolve/main/qwen2.5-3b-instruct-q4_k_m.gguf";
    public const string DefaultModelFileName = "qwen2.5-3b-instruct-q4_k_m.gguf";

    public AiBootstrapper(PortablePaths paths)
    {
        _paths = paths;
        _http = new HttpClient { Timeout = TimeSpan.FromHours(6) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OptiForge", "0.2.1"));
    }

    public async Task<(bool ok, string message)> EnsureRuntimeAsync(
        IProgress<BootstrapProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.LlamaDir);
        var existing = Directory.EnumerateFiles(_paths.LlamaDir, "llama-server*.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (existing is not null)
            return (true, "llama-server đã có: " + existing);

        progress?.Report(new BootstrapProgress { Stage = "runtime", Percent = 0, Detail = "Tìm release llama.cpp…" });
        var zipUrl = await ResolveLlamaZipUrlAsync(ct);
        if (zipUrl is null)
            return (false, "Không tìm thấy asset Windows zip trên llama.cpp releases. Tải thủ công vào Runtime/llama.");

        var zipPath = Path.Combine(_paths.DataDir, "llama-server-download.zip");
        progress?.Report(new BootstrapProgress { Stage = "runtime", Percent = 5, Detail = "Đang tải llama-server…" });
        await DownloadFileAsync(zipUrl, zipPath, p =>
        {
            progress?.Report(new BootstrapProgress
            {
                Stage = "runtime",
                Percent = 5 + p * 0.7,
                Detail = $"Tải runtime… {p:0}%"
            });
        }, ct);

        progress?.Report(new BootstrapProgress { Stage = "runtime", Percent = 80, Detail = "Giải nén…" });
        ZipFile.ExtractToDirectory(zipPath, _paths.LlamaDir, overwriteFiles: true);
        try { File.Delete(zipPath); } catch { /* ignore */ }

        existing = Directory.EnumerateFiles(_paths.LlamaDir, "llama-server*.exe", SearchOption.AllDirectories).FirstOrDefault();
        return existing is null
            ? (false, "Đã giải nén nhưng không thấy llama-server.exe. Kiểm tra Runtime/llama.")
            : (true, "Đã cài llama-server: " + existing);
    }

    public async Task<(bool ok, string message)> EnsureModelAsync(
        IProgress<BootstrapProgress>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(_paths.ModelsFastDir);
        var dest = Path.Combine(_paths.ModelsFastDir, DefaultModelFileName);
        if (File.Exists(dest) && new FileInfo(dest).Length > 100_000_000)
            return (true, "Model đã có: " + dest);

        progress?.Report(new BootstrapProgress
        {
            Stage = "model",
            Percent = 0,
            Detail = "Tải GGUF ~2GB (có thể lâu tùy mạng)…"
        });

        await DownloadFileAsync(DefaultModelUrl, dest + ".partial", p =>
        {
            progress?.Report(new BootstrapProgress
            {
                Stage = "model",
                Percent = p,
                Detail = $"Tải model… {p:0}% (~2GB)"
            });
        }, ct);

        if (File.Exists(dest)) File.Delete(dest);
        File.Move(dest + ".partial", dest);
        return (true, "Đã tải model: " + dest);
    }

    public async Task<(bool ok, string message)> EnsureAllAsync(
        IProgress<BootstrapProgress>? progress = null,
        CancellationToken ct = default)
    {
        var (rok, rmsg) = await EnsureRuntimeAsync(progress, ct);
        if (!rok) return (false, rmsg);
        var (mok, mmsg) = await EnsureModelAsync(progress, ct);
        return (mok, rmsg + " | " + mmsg);
    }

    private async Task<string?> ResolveLlamaZipUrlAsync(CancellationToken ct)
    {
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        // "latest" tag may be a meta release without binaries; scan recent releases instead.
        using var resp = await _http.GetAsync(
            "https://api.github.com/repos/ggml-org/llama.cpp/releases?per_page=8", ct);
        if (!resp.IsSuccessStatusCode) return null;

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        string? best = null;
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("assets", out var assets)) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url = asset.GetProperty("browser_download_url").GetString();
                if (url is null) continue;
                var lower = name.ToLowerInvariant();
                if (!lower.EndsWith(".zip")) continue;

                // Current naming: llama-bNNNN-bin-win-cpu-x64.zip
                if (lower.Contains("win-cpu-x64") ||
                    (lower.Contains("win") && lower.Contains("cpu") && lower.Contains("x64") && !lower.Contains("cuda")))
                    return url;

                if (lower.Contains("win") && lower.Contains("x64") &&
                    !lower.Contains("cuda") && !lower.Contains("vulkan"))
                    best ??= url;
            }
        }

        return best;
    }

    private async Task DownloadFileAsync(
        string url,
        string destPath,
        Action<double> onPercent,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var input = await resp.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(destPath);
        var buffer = new byte[1024 * 256];
        long readTotal = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            readTotal += read;
            if (total > 0)
                onPercent(100.0 * readTotal / total);
            else
                onPercent(0);
        }
    }
}

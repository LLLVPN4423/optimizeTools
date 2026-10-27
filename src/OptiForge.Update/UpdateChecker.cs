using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace OptiForge.Update;

public sealed class UpdateChecker
{
    public string RepositoryUrl { get; } = "https://github.com/LLLVPN4423/optimizeTools";
    private const string ApiLatest = "https://api.github.com/repos/LLLVPN4423/optimizeTools/releases/latest";

    public string CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0";

    public async Task<(bool updateAvailable, string message)> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("OptiForge", CurrentVersion));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var resp = await http.GetAsync(ApiLatest, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return (false,
                    $"Chưa có GitHub Release. Version hiện tại: {CurrentVersion}. Tạo Release tại {RepositoryUrl}/releases");
            }

            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                return (false, $"GitHub API {(int)resp.StatusCode}: {Trim(body)}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
            var htmlUrl = doc.RootElement.TryGetProperty("html_url", out var u)
                ? u.GetString()
                : RepositoryUrl + "/releases";

            if (string.IsNullOrWhiteSpace(tag))
                return (false, "Release không có tag_name.");

            var newer = IsNewer(tag, CurrentVersion);
            return newer
                ? (true, $"Có bản mới: {tag} (đang dùng {CurrentVersion}). {htmlUrl}")
                : (false, $"Đang ở bản mới nhất (hoặc ngang): local {CurrentVersion}, remote {tag}.");
        }
        catch (HttpRequestException)
        {
            return (false, "Offline hoặc không tới được GitHub. Update check cần mạng.");
        }
        catch (TaskCanceledException)
        {
            return (false, "Timeout khi gọi GitHub Releases.");
        }
        catch (Exception ex)
        {
            return (false, "Update check lỗi: " + ex.Message);
        }
    }

    private static bool IsNewer(string remote, string local)
    {
        if (!Version.TryParse(Normalize(remote), out var r)) return false;
        if (!Version.TryParse(Normalize(local), out var l)) return true;
        return r > l;
    }

    private static string Normalize(string v)
    {
        v = v.Trim().TrimStart('v', 'V');
        var parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries);
        while (parts.Length < 3)
            v += ".0";
        // rebuild if we appended incorrectly
        parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
            return $"{parts[0]}.{parts[1]}.{parts[2]}";
        return v;
    }

    private static string Trim(string s) => s.Length <= 200 ? s : s[..200] + "…";
}

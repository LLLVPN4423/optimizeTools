using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace OptiForge.AI;

/// <summary>Minimal OpenAI-compatible chat client (builtin llama-server or LM Studio).</summary>
public sealed class OpenAiCompatClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(6) };

    public async Task<bool> IsReachableAsync(string baseUrl, CancellationToken ct = default)
    {
        try
        {
            var root = baseUrl.TrimEnd('/');
            using var resp = await _http.GetAsync(root + "/models", ct);
            if (resp.IsSuccessStatusCode) return true;
            using var resp2 = await _http.GetAsync(root.Replace("/v1", "") + "/health", ct);
            return resp2.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> ListModelsAsync(string baseUrl, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(baseUrl.TrimEnd('/') + "/models", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var raw = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
            var names = new List<string>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var id))
                    names.Add(id.GetString() ?? "");
            }
            return string.Join(", ", names.Where(n => n.Length > 0).Take(5));
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> ChatAsync(
        string baseUrl,
        string system,
        string user,
        double temperature = 0.3,
        int maxTokens = 1200,
        string model = "local",
        CancellationToken ct = default)
    {
        var bodyObj = new
        {
            model,
            temperature,
            max_tokens = maxTokens,
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user", content = user }
            }
        };
        var json = JsonSerializer.Serialize(bodyObj);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(baseUrl.TrimEnd('/') + "/chat/completions", content, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            return $"API error {(int)resp.StatusCode}: {(raw.Length > 400 ? raw[..400] : raw)}";

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
                   ?? raw;
        }
        catch
        {
            return raw;
        }
    }

    public void Dispose() => _http.Dispose();
}

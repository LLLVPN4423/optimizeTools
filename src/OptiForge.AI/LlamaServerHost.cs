using System.Diagnostics;
using System.Net.Http;
using OptiForge.Core;

namespace OptiForge.AI;

public sealed class LlamaServerHost : IAsyncDisposable
{
    private readonly PortablePaths _paths;
    private readonly int _port;
    private Process? _process;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

    public LlamaServerHost(PortablePaths paths, int port = 8081)
    {
        _paths = paths;
        _port = port;
    }

    public int Port => _port;
    public bool IsRunning => _process is { HasExited: false };
    public string? RuntimePath { get; private set; }
    public string? ModelPath { get; private set; }

    public string? FindRuntime()
    {
        if (!Directory.Exists(_paths.LlamaDir)) return null;
        return Directory.EnumerateFiles(_paths.LlamaDir, "llama-server*.exe", SearchOption.AllDirectories).FirstOrDefault()
               ?? Directory.EnumerateFiles(_paths.LlamaDir, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault();
    }

    public string? FindModel(string preferredTier = "auto")
    {
        string? Pick(string dir) =>
            Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*.gguf", SearchOption.AllDirectories).OrderBy(f => f).FirstOrDefault()
                : null;

        return preferredTier.ToLowerInvariant() switch
        {
            "quality" => Pick(_paths.ModelsQualityDir) ?? Pick(_paths.ModelsFastDir),
            "fast" => Pick(_paths.ModelsFastDir) ?? Pick(_paths.ModelsQualityDir),
            _ => Pick(_paths.ModelsFastDir) ?? Pick(_paths.ModelsQualityDir)
        };
    }

    public async Task<(bool ok, string message)> StartAsync(string preferredTier = "auto", CancellationToken ct = default)
    {
        if (IsRunning)
            return (true, $"Server already running on port {_port}");

        RuntimePath = FindRuntime();
        ModelPath = FindModel(preferredTier);
        if (RuntimePath is null)
            return (false, "Không tìm thấy llama-server.exe trong Runtime/llama.");
        if (ModelPath is null)
            return (false, "Không tìm thấy file .gguf trong Models/fast hoặc Models/quality.");

        var psi = new ProcessStartInfo
        {
            FileName = RuntimePath,
            Arguments = $"-m \"{ModelPath}\" --port {_port} --host 127.0.0.1 -c 2048 -ngl 0",
            WorkingDirectory = Path.GetDirectoryName(RuntimePath) ?? _paths.LlamaDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        _process = Process.Start(psi);
        if (_process is null)
            return (false, "Không start được llama-server process.");

        // Wait until health/openapi responds or timeout
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_process.HasExited)
                return (false, $"llama-server exited early (code {_process.ExitCode}). Check model/runtime.");

            if (await IsReachableAsync(ct))
                return (true, $"Server sẵn sàng — {Path.GetFileName(ModelPath)} @ 127.0.0.1:{_port}");

            await Task.Delay(500, ct);
        }

        return (false, "Timeout chờ llama-server. Model lớn trên HDD có thể cần thêm thời gian.");
    }

    public async Task StopAsync()
    {
        if (_process is null) return;
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"http://127.0.0.1:{_port}/health", ct);
            if (resp.IsSuccessStatusCode) return true;
        }
        catch { /* try props */ }

        try
        {
            using var resp = await _http.GetAsync($"http://127.0.0.1:{_port}/v1/models", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool fromCache, string response)> ChatAsync(
        string prompt,
        PromptCache cache,
        bool useCache = true,
        string? systemPrompt = null,
        string? modeKey = null,
        double temperature = 0.4,
        int maxTokens = 512,
        IReadOnlyList<(string role, string content)>? history = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return (false, "Prompt trống.");

        var modelKey = ModelPath ?? $"port:{_port}";
        var cacheKey = (modeKey ?? "chat") + "\n" + (systemPrompt ?? "") + "\n" + prompt;
        var canCache = useCache && (history is null || history.Count == 0);
        if (canCache && cache.TryGet(modelKey, cacheKey, out var cached, modeKey))
            return (true, cached);

        if (!await IsReachableAsync(ct))
            return (false, "Server chưa chạy. Bấm Start server trước.");

        var messagesJson = BuildMessagesJson(systemPrompt, history, prompt);
        var temp = temperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var body = $$"""
        {
          "model": "local",
          "messages": {{messagesJson}},
          "temperature": {{temp}},
          "max_tokens": {{maxTokens}}
        }
        """;

        using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync($"http://127.0.0.1:{_port}/v1/chat/completions", content, ct);
        var raw = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
        {
            var combined = string.IsNullOrWhiteSpace(systemPrompt)
                ? prompt
                : systemPrompt + "\n\nUser: " + prompt;
            var body2 = $$"""
            {"prompt":{{JsonEscape(combined)}},"n_predict":{{maxTokens}},"temperature":{{temp}}}
            """;
            using var content2 = new StringContent(body2, System.Text.Encoding.UTF8, "application/json");
            using var resp2 = await _http.PostAsync($"http://127.0.0.1:{_port}/completion", content2, ct);
            var raw2 = await resp2.Content.ReadAsStringAsync(ct);
            if (!resp2.IsSuccessStatusCode)
                return (false, $"API error {(int)resp.StatusCode}/{(int)resp2.StatusCode}: {Trim(raw)} | {Trim(raw2)}");

            var text2 = ExtractCompletion(raw2);
            if (canCache) cache.Set(modelKey, cacheKey, text2, modeKey);
            return (false, text2);
        }

        var text = ExtractChat(raw);
        if (canCache) cache.Set(modelKey, cacheKey, text, modeKey);
        return (false, text);
    }

    /// <summary>Stream tokens (SSE) for a ChatGPT-like feel; falls back to non-stream ChatAsync.</summary>
    public async Task<(bool fromCache, string response)> ChatStreamingAsync(
        string prompt,
        PromptCache cache,
        Action<string> onDelta,
        bool useCache = true,
        string? systemPrompt = null,
        string? modeKey = null,
        double temperature = 0.4,
        int maxTokens = 512,
        IReadOnlyList<(string role, string content)>? history = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return (false, "Prompt trống.");

        var modelKey = ModelPath ?? $"port:{_port}";
        var cacheKey = (modeKey ?? "chat") + "\n" + (systemPrompt ?? "") + "\n" + prompt;
        var canCache = useCache && (history is null || history.Count == 0);
        if (canCache && cache.TryGet(modelKey, cacheKey, out var cached, modeKey))
        {
            onDelta(cached);
            return (true, cached);
        }

        if (!await IsReachableAsync(ct))
            return (false, "Server chưa chạy. Bấm Start server trước.");

        var messagesJson = BuildMessagesJson(systemPrompt, history, prompt);
        var temp = temperature.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var body = $$"""
        {
          "model": "local",
          "messages": {{messagesJson}},
          "temperature": {{temp}},
          "max_tokens": {{maxTokens}},
          "stream": true
        }
        """;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{_port}/v1/chat/completions");
        req.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var fallback = await ChatAsync(prompt, cache, useCache, systemPrompt, modeKey, temperature, maxTokens, history, ct);
            if (!fallback.fromCache && !string.IsNullOrEmpty(fallback.response))
                onDelta(fallback.response);
            return fallback;
        }

        var sb = new System.Text.StringBuilder();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line["data:".Length..].Trim();
            if (data is "[DONE]") break;

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(data);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() == 0) continue;
                var delta = choices[0].GetProperty("delta");
                if (delta.TryGetProperty("content", out var c))
                {
                    var piece = c.GetString();
                    if (!string.IsNullOrEmpty(piece))
                    {
                        sb.Append(piece);
                        onDelta(piece);
                    }
                }
            }
            catch
            {
                // skip malformed SSE chunk
            }
        }

        var text = sb.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return await ChatAsync(prompt, cache, useCache, systemPrompt, modeKey, temperature, maxTokens, history, ct);

        if (canCache) cache.Set(modelKey, cacheKey, text, modeKey);
        return (false, text);
    }

    private static string BuildMessagesJson(
        string? systemPrompt,
        IReadOnlyList<(string role, string content)>? history,
        string userPrompt)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            parts.Add($$"""{"role":"system","content":{{JsonEscape(systemPrompt)}}}""");

        if (history is not null)
        {
            foreach (var (role, content) in history)
            {
                if (string.IsNullOrWhiteSpace(content)) continue;
                var r = role is "assistant" or "system" or "user" ? role : "user";
                parts.Add($$"""{"role":"{{r}}","content":{{JsonEscape(content)}}}""");
            }
        }

        parts.Add($$"""{"role":"user","content":{{JsonEscape(userPrompt)}}}""");
        return "[" + string.Join(",", parts) + "]";
    }

    private static string ExtractChat(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
                   ?? json;
        }
        catch
        {
            return json;
        }
    }

    private static string ExtractCompletion(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("content", out var c))
                return c.GetString() ?? json;
        }
        catch { /* fallthrough */ }
        return json;
    }

    private static string JsonEscape(string s) => System.Text.Json.JsonSerializer.Serialize(s);
    private static string Trim(string s) => s.Length <= 300 ? s : s[..300] + "…";

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _http.Dispose();
    }
}

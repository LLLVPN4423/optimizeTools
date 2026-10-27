using OptiForge.Core;

namespace OptiForge.AI;

public sealed class LocalAiService : IAsyncDisposable
{
    private readonly PortablePaths _paths;
    private readonly AppSettings _settings;
    public LlamaServerHost Host { get; }
    public PromptCache Cache { get; }
    public QuotaSaver Quota { get; }

    public LocalAiService(PortablePaths paths, AppSettings settings)
    {
        _paths = paths;
        _settings = settings;
        Host = new LlamaServerHost(paths, settings.AiPort);
        Cache = new PromptCache(paths);
        Quota = new QuotaSaver(paths, Cache);
    }

    public async Task<AiStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var runtime = Host.FindRuntime();
        var model = Host.FindModel(_settings.PreferredModelTier);
        var reachable = await Host.IsReachableAsync(ct);

        return new AiStatus
        {
            RuntimePresent = runtime is not null,
            ModelPresent = model is not null,
            ServerReachable = reachable || Host.IsRunning,
            ActiveModelPath = Host.ModelPath ?? model,
            Message = BuildMessage(runtime, model, reachable || Host.IsRunning)
        };
    }

    public Task<(bool ok, string message)> StartServerAsync(CancellationToken ct = default)
        => Host.StartAsync(_settings.PreferredModelTier, ct);

    public Task StopServerAsync() => Host.StopAsync();

    public async Task<(bool fromCache, string response)> ChatAsync(
        string prompt,
        bool useCache = true,
        AiAssistMode? mode = null,
        IReadOnlyList<(string role, string content)>? history = null,
        CancellationToken ct = default)
    {
        mode ??= AiAssistModes.Default;
        var result = await Host.ChatAsync(
            prompt,
            Cache,
            useCache,
            systemPrompt: mode.SystemPrompt,
            modeKey: mode.Id,
            temperature: mode.Temperature,
            maxTokens: mode.MaxTokens,
            history: history,
            ct: ct);
        if (result.fromCache) Quota.RecordCacheHit();
        else if (!result.response.StartsWith("Server chưa", StringComparison.Ordinal) &&
                 !result.response.StartsWith("Prompt trống", StringComparison.Ordinal) &&
                 !result.response.StartsWith("API error", StringComparison.Ordinal))
            Quota.RecordLocalGeneration();
        return result;
    }

    public async Task<(bool fromCache, string response)> ChatStreamingAsync(
        string prompt,
        Action<string> onDelta,
        bool useCache = true,
        AiAssistMode? mode = null,
        IReadOnlyList<(string role, string content)>? history = null,
        CancellationToken ct = default)
    {
        mode ??= AiAssistModes.Default;
        var result = await Host.ChatStreamingAsync(
            prompt,
            Cache,
            onDelta,
            useCache,
            systemPrompt: mode.SystemPrompt,
            modeKey: mode.Id,
            temperature: mode.Temperature,
            maxTokens: mode.MaxTokens,
            history: history,
            ct: ct);
        if (result.fromCache) Quota.RecordCacheHit();
        else if (!string.IsNullOrWhiteSpace(result.response) &&
                 !result.response.StartsWith("Server chưa", StringComparison.Ordinal) &&
                 !result.response.StartsWith("Prompt trống", StringComparison.Ordinal) &&
                 !result.response.StartsWith("API error", StringComparison.Ordinal))
            Quota.RecordLocalGeneration();
        return result;
    }

    private static string BuildMessage(string? runtime, string? model, bool running)
    {
        if (running)
            return "llama-server đang chạy — có thể chat.";
        if (runtime is null && model is null)
            return "Chưa có llama-server và model GGUF. Xem Runtime/llama/README.md và Models/fast/README.md.";
        if (runtime is null)
            return "Đã có model, thiếu llama-server trong Runtime/llama.";
        if (model is null)
            return "Đã có runtime, thiếu file .gguf trong Models/fast (khuyến nghị Qwen2.5 3B Q4_K_M).";
        return "Runtime + model sẵn sàng. Bấm Start server để chat offline.";
    }

    public ValueTask DisposeAsync() => Host.DisposeAsync();
}

using OptiForge.Core;

namespace OptiForge.AI;

public sealed class LocalAiService : IAsyncDisposable
{
    private readonly PortablePaths _paths;
    private readonly AppSettings _settings;
    public LlamaServerHost Host { get; }
    public PromptCache Cache { get; }

    public LocalAiService(PortablePaths paths, AppSettings settings)
    {
        _paths = paths;
        _settings = settings;
        Host = new LlamaServerHost(paths, settings.AiPort);
        Cache = new PromptCache(paths);
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

    public Task<(bool fromCache, string response)> ChatAsync(string prompt, bool useCache = true, CancellationToken ct = default)
        => Host.ChatAsync(prompt, Cache, useCache, ct);

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

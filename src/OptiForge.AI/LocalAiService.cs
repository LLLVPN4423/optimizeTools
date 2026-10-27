using OptiForge.Core;

namespace OptiForge.AI;

/// <summary>
/// Phase 5 will start llama-server and chat. For now: detect runtime/models on the portable drive.
/// </summary>
public sealed class LocalAiService
{
    private readonly PortablePaths _paths;

    public LocalAiService(PortablePaths paths)
    {
        _paths = paths;
    }

    public AiStatus GetStatus()
    {
        var runtimeExe = Directory.Exists(_paths.LlamaDir)
            ? Directory.EnumerateFiles(_paths.LlamaDir, "llama-server*.exe", SearchOption.AllDirectories).FirstOrDefault()
              ?? Directory.EnumerateFiles(_paths.LlamaDir, "llama-server", SearchOption.AllDirectories).FirstOrDefault()
            : null;

        var model = FindPreferredModel();

        return new AiStatus
        {
            RuntimePresent = runtimeExe is not null,
            ModelPresent = model is not null,
            ServerReachable = false,
            ActiveModelPath = model,
            Message = BuildMessage(runtimeExe, model)
        };
    }

    private string? FindPreferredModel()
    {
        string? Pick(string dir) =>
            Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir, "*.gguf", SearchOption.AllDirectories).OrderBy(f => f).FirstOrDefault()
                : null;

        return Pick(_paths.ModelsFastDir) ?? Pick(_paths.ModelsQualityDir);
    }

    private static string BuildMessage(string? runtime, string? model)
    {
        if (runtime is null && model is null)
            return "Chưa có llama-server và model GGUF. Xem Runtime/llama/README.md và Models/fast/README.md.";
        if (runtime is null)
            return "Đã có model, thiếu llama-server trong Runtime/llama.";
        if (model is null)
            return "Đã có runtime, thiếu file .gguf trong Models/fast (khuyến nghị Qwen2.5 3B Q4_K_M).";
        return "Runtime + model sẵn sàng. Chat API sẽ kích hoạt ở Phase 5.";
    }
}

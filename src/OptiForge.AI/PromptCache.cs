using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OptiForge.Core;

namespace OptiForge.AI;

public sealed class PromptCache
{
    private readonly string _dir;

    public PromptCache(PortablePaths paths)
    {
        _dir = Path.Combine(paths.DataDir, "prompt-cache");
        Directory.CreateDirectory(_dir);
    }

    public bool TryGet(string modelKey, string prompt, out string response, string? modeKey = null)
    {
        response = "";
        var path = PathFor(modelKey, prompt, modeKey);
        if (!File.Exists(path)) return false;
        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            response = doc.RootElement.GetProperty("response").GetString() ?? "";
            return !string.IsNullOrWhiteSpace(response);
        }
        catch
        {
            return false;
        }
    }

    public void Set(string modelKey, string prompt, string response, string? modeKey = null)
    {
        var path = PathFor(modelKey, prompt, modeKey);
        var payload = JsonSerializer.Serialize(new
        {
            model = modelKey,
            mode = modeKey,
            prompt,
            response,
            savedAt = DateTimeOffset.UtcNow
        });
        File.WriteAllText(path, payload);
    }

    private string PathFor(string modelKey, string prompt, string? modeKey)
    {
        var raw = modelKey + "\n" + (modeKey ?? "") + "\n" + prompt;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return Path.Combine(_dir, hash + ".json");
    }
}

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

    public bool TryGet(string modelKey, string prompt, out string response)
    {
        response = "";
        var path = PathFor(modelKey, prompt);
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

    public void Set(string modelKey, string prompt, string response)
    {
        var path = PathFor(modelKey, prompt);
        var payload = JsonSerializer.Serialize(new
        {
            model = modelKey,
            prompt,
            response,
            savedAt = DateTimeOffset.UtcNow
        });
        File.WriteAllText(path, payload);
    }

    private string PathFor(string modelKey, string prompt)
    {
        var raw = modelKey + "\n" + prompt;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
        return Path.Combine(_dir, hash + ".json");
    }
}

using System.Diagnostics;
using System.Text;
using OptiForge.Core;

namespace OptiForge.AI;

public sealed class QuotaStats
{
    public int CacheEntries { get; init; }
    public int CacheHitsLifetime { get; init; }
    public int LocalGenerationsLifetime { get; init; }
    public int CloudBriefsCopied { get; init; }
    public string Summary { get; init; } = "";
}

/// <summary>
/// Helps stretch ChatGPT / Gemini / Claude free tiers by doing routine work local-first,
/// then shipping a compact "polish brief" to the cloud model.
/// </summary>
public sealed class QuotaSaver
{
    private readonly string _statsPath;
    private readonly PromptCache _cache;
    private int _hits;
    private int _gens;
    private int _briefs;

    public QuotaSaver(PortablePaths paths, PromptCache cache)
    {
        _cache = cache;
        _statsPath = Path.Combine(paths.DataDir, "quota-stats.json");
        Load();
    }

    public static IReadOnlyList<string> FreeTierTips { get; } =
    [
        "Local-first: dich / tom tat / viet lai / checklist → dung AI tab (khong ton quota cloud).",
        "Cloud chi cho viec kho: ly luan sau, code lon, research, ban cuoi can chat luong.",
        "Draft local → Copy cloud brief → dan vao ChatGPT/Gemini/Claude (1 tin ngan thay vi 10 tin lan).",
        "Batch: gop nhieu cau hoi lien quan vao 1 prompt cloud thay vi gui tung cai.",
        "Doi chu de → chat moi tren cloud; mang sang chi ban tom tat (brief), khong paste ca thread.",
        "Xoay vong free tier: Gemini (thuong rong) + ChatGPT + Claude khi mot ben het limit (~5h).",
        "Tat search/thinking/tool khong can tren cloud — moi lan goi tốn limit nhanh hon.",
        "Prompt cache OptiForge: cau trung khong goi model lai (offline + tiet kiem thoi gian)."
    ];

    public static IReadOnlyList<(string name, string url)> CloudSites { get; } =
    [
        ("ChatGPT", "https://chatgpt.com/"),
        ("Gemini", "https://gemini.google.com/app"),
        ("Claude", "https://claude.ai/new"),
        ("Perplexity", "https://www.perplexity.ai/")
    ];

    public void RecordCacheHit()
    {
        _hits++;
        Save();
    }

    public void RecordLocalGeneration()
    {
        _gens++;
        Save();
    }

    public void RecordCloudBrief()
    {
        _briefs++;
        Save();
    }

    public QuotaStats GetStats()
    {
        var entries = _cache.CountEntries();
        return new QuotaStats
        {
            CacheEntries = entries,
            CacheHitsLifetime = _hits,
            LocalGenerationsLifetime = _gens,
            CloudBriefsCopied = _briefs,
            Summary =
                $"Cache: {entries} entries, {_hits} hits | Local gens: {_gens} | Cloud briefs: {_briefs} " +
                $"(moi hit/local gen = bot bot duoc 1 request cloud)."
        };
    }

    /// <summary>
    /// Builds a short paste-ready prompt for ChatGPT/Gemini/Claude so you spend 1 cloud message, not many.
    /// </summary>
    public string BuildCloudPolishBrief(
        string task,
        IReadOnlyList<(string role, string content)>? localHistory,
        string? lastLocalAnswer = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a senior assistant. I drafted locally with a small offline model (Qwen 3B).");
        sb.AppendLine("Please improve quality: fix errors, deepen reasoning, keep my intent. Be concise.");
        sb.AppendLine();
        sb.AppendLine("## My goal");
        sb.AppendLine(string.IsNullOrWhiteSpace(task) ? "(see conversation)" : task.Trim());
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(lastLocalAnswer))
        {
            sb.AppendLine("## Local draft");
            sb.AppendLine(Trim(lastLocalAnswer!, 2500));
            sb.AppendLine();
        }

        if (localHistory is { Count: > 0 })
        {
            sb.AppendLine("## Local context (compressed)");
            var take = localHistory.Count <= 6 ? localHistory : localHistory.Skip(localHistory.Count - 6).ToList();
            foreach (var (role, content) in take)
                sb.AppendLine($"- {role}: {Trim(content, 400)}");
            sb.AppendLine();
        }

        sb.AppendLine("## Output");
        sb.AppendLine("Return the improved final answer only. Use Vietnamese if my goal is Vietnamese.");
        return sb.ToString().Trim();
    }

    public string BuildBatchPrompt(IEnumerable<string> tasks)
    {
        var list = tasks.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Take(12).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Please answer ALL items in one reply. Number answers 1..N. Keep each answer short.");
        sb.AppendLine();
        for (var i = 0; i < list.Count; i++)
            sb.AppendLine($"{i + 1}. {list[i]}");
        return sb.ToString();
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
    }

    private static string Trim(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private void Load()
    {
        try
        {
            if (!File.Exists(_statsPath)) return;
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(_statsPath));
            var r = doc.RootElement;
            if (r.TryGetProperty("hits", out var h)) _hits = h.GetInt32();
            if (r.TryGetProperty("gens", out var g)) _gens = g.GetInt32();
            if (r.TryGetProperty("briefs", out var b)) _briefs = b.GetInt32();
        }
        catch { /* ignore */ }
    }

    private void Save()
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new { hits = _hits, gens = _gens, briefs = _briefs });
            File.WriteAllText(_statsPath, json);
        }
        catch { /* ignore */ }
    }
}

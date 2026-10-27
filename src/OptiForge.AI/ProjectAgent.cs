using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using OptiForge.Core;

namespace OptiForge.AI;

public sealed class AgentFileSpec
{
    public string RelativePath { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class AgentPlan
{
    public string ProjectName { get; set; } = "new-project";
    public string Template { get; set; } = "html-static";
    public string Summary { get; set; } = "";
    public List<AgentFileSpec> Files { get; set; } = [];
    public List<string> NextSteps { get; set; } = [];
    public string RawModelOutput { get; set; } = "";
    public string BackendUsed { get; set; } = "";
}

public sealed class AgentRunResult
{
    public bool Ok { get; init; }
    public string Message { get; init; } = "";
    public string? ProjectPath { get; init; }
    public int FilesWritten { get; init; }
}

/// <summary>
/// Local "create a project from chat" agent — template-first harness (works better on 3B–7B than pure tool loops).
/// Writes only under {Root}/Projects/{safeName}/.
/// </summary>
public sealed class ProjectAgent
{
    private readonly PortablePaths _paths;
    private readonly OpenAiCompatClient _client = new();

    public static IReadOnlyList<string> Templates { get; } =
        ["html-static", "csharp-console", "python-cli", "nodejs-mini"];

    public ProjectAgent(PortablePaths paths) => _paths = paths;

    public string ProjectsRoot => Path.Combine(_paths.Root, "Projects");

    public async Task<(bool ok, string message)> ProbeBackendAsync(
        string backend, // builtin | lmstudio
        string lmStudioUrl,
        int builtinPort,
        CancellationToken ct = default)
    {
        if (backend.Equals("lmstudio", StringComparison.OrdinalIgnoreCase))
        {
            var baseUrl = NormalizeBase(lmStudioUrl);
            var up = await _client.IsReachableAsync(baseUrl, ct);
            if (!up)
                return (false, $"LM Studio unreachable at {baseUrl}. Open LM Studio → Developer → Start server.");
            var models = await _client.ListModelsAsync(baseUrl, ct);
            return (true, $"LM Studio OK — models: {models ?? "(loaded none?)"}");
        }

        var builtin = $"http://127.0.0.1:{builtinPort}/v1";
        var bUp = await _client.IsReachableAsync(builtin, ct);
        return bUp
            ? (true, $"Builtin llama-server OK @ {builtin}")
            : (false, "Builtin server not running. Start AI tab first (or switch to LM Studio).");
    }

    public async Task<AgentPlan> PlanAsync(
        string userRequest,
        string backend,
        string lmStudioUrl,
        int builtinPort,
        string? preferredTemplate,
        CancellationToken ct = default)
    {
        var template = string.IsNullOrWhiteSpace(preferredTemplate) || preferredTemplate == "auto"
            ? GuessTemplate(userRequest)
            : preferredTemplate.Trim();

        var system = """
            You are OptiForge Local Agent. Output ONLY valid JSON (no markdown fences).
            Goal: plan a small starter project the user can open immediately.
            Schema:
            {
              "projectName": "kebab-or-snake-case-short",
              "template": "html-static|csharp-console|python-cli|nodejs-mini",
              "summary": "one sentence",
              "files": [{"path":"relative/path","content":"...file body..."}],
              "nextSteps": ["step1","step2"]
            }
            Rules:
            - 2 to 6 files max. Keep each file short.
            - Paths relative, no .., no absolute drives.
            - Prefer Vietnamese comments only if user wrote Vietnamese; code identifiers in English.
            - Include a short README.md.
            - Do not invent huge frameworks.
            """;

        var user = $"""
            User request: {userRequest}
            Preferred template hint: {template}
            Available templates: {string.Join(", ", Templates)}
            """;

        var baseUrl = backend.Equals("lmstudio", StringComparison.OrdinalIgnoreCase)
            ? NormalizeBase(lmStudioUrl)
            : $"http://127.0.0.1:{builtinPort}/v1";

        var raw = await _client.ChatAsync(baseUrl, system, user, temperature: 0.25, maxTokens: 1600, ct: ct);
        var plan = ParsePlan(raw, template);
        plan.RawModelOutput = raw;
        plan.BackendUsed = backend;
        if (string.IsNullOrWhiteSpace(plan.Template))
            plan.Template = template;
        EnsureSeedFiles(plan);
        return plan;
    }

    public AgentRunResult Execute(AgentPlan plan, bool dryRun)
    {
        var name = SanitizeName(plan.ProjectName);
        if (string.IsNullOrWhiteSpace(name))
            return new AgentRunResult { Ok = false, Message = "Invalid project name." };

        var dest = Path.Combine(ProjectsRoot, name);
        if (dryRun)
        {
            return new AgentRunResult
            {
                Ok = true,
                ProjectPath = dest,
                FilesWritten = plan.Files.Count,
                Message = $"[Dry-run] Would create {plan.Files.Count} file(s) under {dest}"
            };
        }

        Directory.CreateDirectory(dest);
        var written = 0;
        foreach (var f in plan.Files)
        {
            var rel = NormalizeRelPath(f.RelativePath);
            if (rel is null) continue;
            var full = Path.GetFullPath(Path.Combine(dest, rel));
            if (!full.StartsWith(Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, f.Content ?? "", Encoding.UTF8);
            written++;
        }

        return new AgentRunResult
        {
            Ok = written > 0,
            ProjectPath = dest,
            FilesWritten = written,
            Message = written > 0
                ? $"Created {written} file(s) → {dest}"
                : "No files written (invalid paths?)."
        };
    }

    private static string GuessTemplate(string request)
    {
        var t = request.ToLowerInvariant();
        if (t.Contains("python") || t.Contains("fastapi") || t.Contains(".py")) return "python-cli";
        if (t.Contains("node") || t.Contains("express") || t.Contains("javascript") || t.Contains("typescript"))
            return "nodejs-mini";
        if (t.Contains("c#") || t.Contains("csharp") || t.Contains("dotnet") || t.Contains(".net") || t.Contains("console"))
            return "csharp-console";
        if (t.Contains("web") || t.Contains("html") || t.Contains("landing") || t.Contains("website"))
            return "html-static";
        return "html-static";
    }

    private static void EnsureSeedFiles(AgentPlan plan)
    {
        if (plan.Files.Count > 0) return;

        plan.Summary = string.IsNullOrWhiteSpace(plan.Summary)
            ? "Starter scaffold (fallback — model JSON parse failed)."
            : plan.Summary;

        plan.Files = plan.Template switch
        {
            "csharp-console" =>
            [
                new AgentFileSpec
                {
                    RelativePath = "Program.cs",
                    Content = """
                    Console.WriteLine("Hello from OptiForge Local Agent");
                    """
                },
                new AgentFileSpec
                {
                    RelativePath = "README.md",
                    Content = $"# {plan.ProjectName}\n\n{plan.Summary}\n\n```bat\ndotnet new console -n {plan.ProjectName}\n```\n"
                }
            ],
            "python-cli" =>
            [
                new AgentFileSpec
                {
                    RelativePath = "main.py",
                    Content = "def main():\n    print('Hello from OptiForge Local Agent')\n\nif __name__ == '__main__':\n    main()\n"
                },
                new AgentFileSpec
                {
                    RelativePath = "README.md",
                    Content = $"# {plan.ProjectName}\n\n{plan.Summary}\n\n```bat\npython main.py\n```\n"
                }
            ],
            "nodejs-mini" =>
            [
                new AgentFileSpec
                {
                    RelativePath = "package.json",
                    Content =
                        "{\"name\":\"" + plan.ProjectName +
                        "\",\"version\":\"0.1.0\",\"private\":true,\"scripts\":{\"start\":\"node index.js\"}}\n"
                },
                new AgentFileSpec
                {
                    RelativePath = "index.js",
                    Content = "console.log('Hello from OptiForge Local Agent');\n"
                },
                new AgentFileSpec
                {
                    RelativePath = "README.md",
                    Content = $"# {plan.ProjectName}\n\n{plan.Summary}\n\n```bat\nnode index.js\n```\n"
                }
            ],
            _ =>
            [
                new AgentFileSpec
                {
                    RelativePath = "index.html",
                    Content =
                        "<!doctype html>\n<html lang=\"vi\">\n<head>\n" +
                        "  <meta charset=\"utf-8\"/>\n" +
                        "  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>\n" +
                        "  <title>" + plan.ProjectName + "</title>\n" +
                        "  <link rel=\"stylesheet\" href=\"styles.css\"/>\n</head>\n<body>\n  <main>\n" +
                        "    <h1>" + plan.ProjectName + "</h1>\n" +
                        "    <p>" + plan.Summary + "</p>\n  </main>\n" +
                        "  <script src=\"app.js\"></script>\n</body>\n</html>\n"
                },
                new AgentFileSpec
                {
                    RelativePath = "styles.css",
                    Content = "body{font-family:system-ui,sans-serif;margin:2rem;background:#121418;color:#e8eaed}main{max-width:40rem}\n"
                },
                new AgentFileSpec
                {
                    RelativePath = "app.js",
                    Content = "console.log('OptiForge Local Agent');\n"
                },
                new AgentFileSpec
                {
                    RelativePath = "README.md",
                    Content = $"# {plan.ProjectName}\n\n{plan.Summary}\n\nOpen `index.html` in a browser.\n"
                }
            ]
        };

        plan.NextSteps =
        [
            "Open the project folder",
            "Edit files as needed",
            "For harder logic: Quota → Cloud brief → Claude/ChatGPT"
        ];
    }

    private static AgentPlan ParsePlan(string raw, string fallbackTemplate)
    {
        var plan = new AgentPlan { Template = fallbackTemplate, ProjectName = "new-project" };
        var json = ExtractJson(raw);
        if (json is null) return plan;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.TryGetProperty("projectName", out var n)) plan.ProjectName = n.GetString() ?? plan.ProjectName;
            if (r.TryGetProperty("template", out var t)) plan.Template = t.GetString() ?? plan.Template;
            if (r.TryGetProperty("summary", out var s)) plan.Summary = s.GetString() ?? "";
            if (r.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in files.EnumerateArray())
                {
                    var path = f.TryGetProperty("path", out var p) ? p.GetString() : null;
                    var content = f.TryGetProperty("content", out var c) ? c.GetString() : null;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    plan.Files.Add(new AgentFileSpec { RelativePath = path!, Content = content ?? "" });
                }
            }
            if (r.TryGetProperty("nextSteps", out var steps) && steps.ValueKind == JsonValueKind.Array)
            {
                foreach (var step in steps.EnumerateArray())
                {
                    var v = step.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) plan.NextSteps.Add(v!);
                }
            }
        }
        catch
        {
            // keep fallback
        }

        return plan;
    }

    private static string? ExtractJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        if (t.StartsWith("```"))
        {
            t = Regex.Replace(t, "^```(?:json)?\\s*", "", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "\\s*```$", "");
        }
        var start = t.IndexOf('{');
        var end = t.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return t[start..(end + 1)];
    }

    private static string SanitizeName(string name)
    {
        var s = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9\-_]+", "-");
        s = Regex.Replace(s, "-{2,}", "-").Trim('-');
        return string.IsNullOrWhiteSpace(s) ? "new-project" : s[..Math.Min(s.Length, 48)];
    }

    private static string? NormalizeRelPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Replace('\\', '/').Trim().TrimStart('/');
        if (p.Contains("..", StringComparison.Ordinal) || p.Contains(':')) return null;
        return p;
    }

    private static string NormalizeBase(string url)
    {
        var u = string.IsNullOrWhiteSpace(url) ? "http://127.0.0.1:1234/v1" : url.Trim().TrimEnd('/');
        if (!u.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            u += "/v1";
        return u;
    }
}

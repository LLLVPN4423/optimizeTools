using System.Text.Json;
using OptiForge.Core.Models;

namespace OptiForge.Core;

public sealed class PlaybookStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PortablePaths _paths;

    public PlaybookStore(PortablePaths paths) => _paths = paths;

    public IReadOnlyList<Playbook> LoadAll()
    {
        var dir = Path.Combine(_paths.KnowledgeDir, "playbooks");
        if (!Directory.Exists(dir))
            return [];

        var list = new List<Playbook>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var pb = JsonSerializer.Deserialize<Playbook>(json, JsonOptions);
                if (pb is not null && !string.IsNullOrWhiteSpace(pb.Title))
                    list.Add(pb);
            }
            catch
            {
                // skip bad files
            }
        }

        return list.OrderBy(p => p.Title).ToList();
    }
}

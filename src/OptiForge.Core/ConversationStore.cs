using System.Text.Json;
using System.Text.Json.Serialization;
using OptiForge.Core.Models;

namespace OptiForge.Core;

/// <summary>File-based conversation persistence (schema v1). No destructive migration.</summary>
public sealed class ConversationStore
{
    public const int SchemaVersion = 1;
    private readonly string _dir;
    private readonly string _indexPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ConversationStore(PortablePaths paths)
    {
        _dir = Path.Combine(paths.DataDir, "conversations");
        _indexPath = Path.Combine(_dir, "index.json");
        Directory.CreateDirectory(_dir);
    }

    public IReadOnlyList<ConversationIndexEntry> List(bool includeArchived = false)
    {
        var index = LoadIndex();
        return index
            .Where(e => includeArchived || !e.Archived)
            .OrderByDescending(e => e.Pinned)
            .ThenByDescending(e => e.UpdatedAt)
            .ToList();
    }

    public Conversation? Get(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<Conversation>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public Conversation Create(string? title = null)
    {
        var c = new Conversation
        {
            Title = string.IsNullOrWhiteSpace(title) ? "New chat" : title.Trim()
        };
        Save(c);
        return c;
    }

    public void Save(Conversation c)
    {
        c.UpdatedAt = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(c.Title) || c.Title == "New chat")
        {
            var firstUser = c.Messages.FirstOrDefault(m => m.Role == "user");
            if (firstUser is not null)
                c.Title = MakeTitle(firstUser.Content);
        }

        var path = PathFor(c.Id);
        File.WriteAllText(path, JsonSerializer.Serialize(c, JsonOptions));
        UpsertIndex(c);
    }

    public bool Delete(string id)
    {
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
        var index = LoadIndex();
        var next = index.Where(e => e.Id != id).ToList();
        WriteIndex(next);
        return true;
    }

    public void SetPinned(string id, bool pinned)
    {
        var c = Get(id);
        if (c is null) return;
        c.Pinned = pinned;
        Save(c);
    }

    public IReadOnlyList<ConversationIndexEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return List();
        var q = query.Trim();
        var hits = new List<ConversationIndexEntry>();
        foreach (var e in List(includeArchived: true))
        {
            if (e.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(e);
                continue;
            }
            var c = Get(e.Id);
            if (c?.Messages.Any(m => m.Content.Contains(q, StringComparison.OrdinalIgnoreCase)) == true)
                hits.Add(e);
        }
        return hits;
    }

    private void UpsertIndex(Conversation c)
    {
        var index = LoadIndex();
        index.RemoveAll(e => e.Id == c.Id);
        index.Add(new ConversationIndexEntry
        {
            Id = c.Id,
            Title = c.Title,
            UpdatedAt = c.UpdatedAt,
            Pinned = c.Pinned,
            Archived = c.Archived,
            MessageCount = c.Messages.Count
        });
        WriteIndex(index);
    }

    private List<ConversationIndexEntry> LoadIndex()
    {
        try
        {
            if (!File.Exists(_indexPath)) return [];
            return JsonSerializer.Deserialize<List<ConversationIndexEntry>>(File.ReadAllText(_indexPath), JsonOptions)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void WriteIndex(List<ConversationIndexEntry> index)
        => File.WriteAllText(_indexPath, JsonSerializer.Serialize(index, JsonOptions));

    private string PathFor(string id) => Path.Combine(_dir, id + ".json");

    private static string MakeTitle(string content)
    {
        var t = content.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (t.Length == 0) return "New chat";
        return t.Length <= 42 ? t : t[..42] + "…";
    }
}

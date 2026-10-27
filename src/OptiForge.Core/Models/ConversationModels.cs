namespace OptiForge.Core.Models;

public sealed class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Role { get; set; } = "user"; // user | assistant | system
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Incomplete { get; set; }
}

public sealed class Conversation
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New chat";
    /// <summary>When true, auto-title from first message will not overwrite.</summary>
    public bool TitleManual { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ModelHint { get; set; }
    public string? ProjectId { get; set; }
    public bool Pinned { get; set; }
    public bool Archived { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<ChatMessage> Messages { get; set; } = [];
}

public sealed class ConversationIndexEntry
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
    public bool Pinned { get; set; }
    public bool Archived { get; set; }
    public int MessageCount { get; set; }

    /// <summary>UI label only — not persisted.</summary>
    public string ListLabel => Pinned ? "📌 " + Title : Title;
}

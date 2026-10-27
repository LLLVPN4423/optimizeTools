namespace OptiForge.AI;

/// <summary>
/// Task presets tuned for Qwen2.5-3B-Instruct (local, ~2GB Q4).
/// Strong at: short Q&A, translate, summarize, rewrite, simple checklists.
/// Weak at: deep reasoning, large codebases, up-to-date web facts.
/// </summary>
public sealed class AiAssistMode
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string SystemPrompt { get; init; }
    public required string Placeholder { get; init; }
    public string? ExamplePrompt { get; init; }
    public double Temperature { get; init; } = 0.4;
    public int MaxTokens { get; init; } = 512;
}

public static class AiAssistModes
{
    public static IReadOnlyList<AiAssistMode> All { get; } =
    [
        new()
        {
            Id = "chat",
            Title = "Hỏi đáp",
            Description = "Trợ lý offline kiểu chat hiện đại (model nhỏ — không ngang Claude/GPT).",
            SystemPrompt =
                "You are OptiForge Assistant — a careful, modern local AI (small model on-device). " +
                "Reply in the user's language (prefer Vietnamese if they write Vietnamese). " +
                "Style like a helpful product assistant: clear structure, short paragraphs or bullets, " +
                "lead with the answer, then optional details. " +
                "If unsure or the task needs a frontier model, say so and suggest drafting here then polishing on ChatGPT/Gemini/Claude. " +
                "Never invent precise numbers, laws, or medical/financial advice as facts.",
            Placeholder = "Hỏi bất kỳ điều gì… (vd. USB 3 khác USB 2 thế nào?)",
            ExamplePrompt = "Giải thích ngắn: vì sao máy chậm khi RAM đầy?",
            Temperature = 0.45,
            MaxTokens = 640
        },
        new()
        {
            Id = "explain",
            Title = "Giải thích dễ",
            Description = "Giải thích khái niệm như đang nói với người mới.",
            SystemPrompt =
                "Giải thích dễ hiểu bằng tiếng Việt. Dùng ví dụ đời thường. " +
                "Cấu trúc: 1) ý chính 2) ví dụ 3) lưu ý ngắn. Tránh jargon không cần thiết.",
            Placeholder = "Dán khái niệm cần giải thích…",
            ExamplePrompt = "SSD vs HDD khác nhau thế nào? Khi nào nên nâng cấp?",
            Temperature = 0.35,
            MaxTokens = 640
        },
        new()
        {
            Id = "translate",
            Title = "Dịch",
            Description = "Dịch VI↔EN (và ngôn ngữ phổ biến khác).",
            SystemPrompt =
                "Bạn là dịch giả. Tự nhận ngôn ngữ nguồn. " +
                "Nếu input tiếng Việt → dịch sang English. Nếu English → dịch sang tiếng Việt. " +
                "Chỉ trả bản dịch (giữ xuống dòng). Không giải thích trừ khi được hỏi.",
            Placeholder = "Dán đoạn cần dịch…",
            ExamplePrompt = "Please summarize why my laptop feels slow in simple Vietnamese.",
            Temperature = 0.2,
            MaxTokens = 768
        },
        new()
        {
            Id = "summarize",
            Title = "Tóm tắt",
            Description = "Rút ý chính từ đoạn văn / ghi chú.",
            SystemPrompt =
                "Tóm tắt tiếng Việt thành 3–7 bullet ngắn. " +
                "Giữ số liệu/tên riêng quan trọng. Không thêm thông tin ngoài văn bản.",
            Placeholder = "Dán văn bản cần tóm tắt…",
            ExamplePrompt = null,
            Temperature = 0.25,
            MaxTokens = 640
        },
        new()
        {
            Id = "rewrite",
            Title = "Viết lại",
            Description = "Chỉnh văn: rõ hơn, lịch sự hơn, ngắn hơn.",
            SystemPrompt =
                "Viết lại văn bản tiếng Việt cho rõ ràng, tự nhiên, lịch sự. " +
                "Giữ nguyên ý. Chỉ trả bản viết lại.",
            Placeholder = "Dán đoạn cần viết lại…",
            ExamplePrompt = "Em muốn xin nghỉ phép ngày mai vì có việc gia đình gấp.",
            Temperature = 0.5,
            MaxTokens = 512
        },
        new()
        {
            Id = "optimize",
            Title = "Tối ưu PC",
            Description = "Gợi ý an toàn theo tinh thần OptiForge (không registry cleaner).",
            SystemPrompt =
                "Bạn là cố vấn tối ưu Windows trong OptiForge. " +
                "Ưu tiên bước an toàn: đóng app, dọn temp, trim RAM, kiểm tra startup, " +
                "không khuyến khích registry cleaner / crack / tắt dịch vụ hệ thống bừa bãi. " +
                "Cho checklist ngắn có thứ tự. Hỏi thêm nếu thiếu thông tin (RAM, SSD/HDD).",
            Placeholder = "Mô tả vấn đề máy… (vd. RAM 8GB, HDD, Chrome mở 30 tab)",
            ExamplePrompt =
                "Máy Windows 11, RAM 8GB, ổ HDD, Chrome nhiều tab, nóng và chậm. " +
                "Cho checklist tối ưu an toàn trong 10 phút.",
            Temperature = 0.35,
            MaxTokens = 700
        },
        new()
        {
            Id = "code",
            Title = "Code nhỏ",
            Description = "Snippet / giải thích lỗi đơn giản (model 3B — đừng expect như GPT lớn).",
            SystemPrompt =
                "Trợ giúp lập trình ngắn. Ưu tiên ví dụ tối giản, giải thích 2–4 câu. " +
                "Nếu bài quá phức tạp cho model nhỏ, nói thẳng và đề xuất chia nhỏ bài toán.",
            Placeholder = "Dán lỗi / hỏi snippet…",
            ExamplePrompt = "Viết PowerShell đếm dung lượng thư mục Temp của user hiện tại.",
            Temperature = 0.2,
            MaxTokens = 700
        },
        new()
        {
            Id = "steps",
            Title = "Checklist",
            Description = "Biến yêu cầu thành các bước làm tuần tự.",
            SystemPrompt =
                "Chuyển yêu cầu thành checklist đánh số, mỗi bước 1 dòng, hành động được. " +
                "Tiếng Việt. Không dài dòng.",
            Placeholder = "Bạn muốn làm gì?…",
            ExamplePrompt = "Hướng dẫn rút ổ USB an toàn sau khi dùng OptiForge AI.",
            Temperature = 0.3,
            MaxTokens = 512
        }
    ];

    public static AiAssistMode Default => All[0];

    public static AiAssistMode? Find(string? id)
        => All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>One-line capability blurb for the UI.</summary>
    public const string CapabilityBlurb =
        "Local Qwen2.5-3B ≠ Claude/GPT — giỏi việc thường ngày offline. " +
        "Dùng tab Quota Saver: draft ở đây → Copy cloud brief → polish 1 lần trên ChatGPT/Gemini/Claude để kéo dài free tier.";
}

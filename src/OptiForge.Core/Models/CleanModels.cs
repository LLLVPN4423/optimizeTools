namespace OptiForge.Core.Models;

public sealed class CleanCategoryDef
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }
    public bool SelectedByDefault { get; init; } = true;
    public int MinAgeDays { get; init; } = 1;
}

public sealed class CleanCategoryResult
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public long BytesFound { get; set; }
    public int FileCount { get; set; }
    public long BytesDeleted { get; set; }
    public int FilesDeleted { get; set; }
    public int FilesFailed { get; set; }
    public bool Selected { get; set; } = true;
    public string? Note { get; set; }
}

public sealed class CleanReport
{
    public bool DryRun { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset FinishedAt { get; set; }
    public List<CleanCategoryResult> Categories { get; init; } = [];
    public long TotalBytesFound => Categories.Where(c => c.Selected).Sum(c => c.BytesFound);
    public long TotalBytesDeleted => Categories.Sum(c => c.BytesDeleted);
    public string Summary { get; set; } = "";
}

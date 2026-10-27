namespace OptiForge.Core.Models;

public sealed class Playbook
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Steps { get; set; } = [];
}

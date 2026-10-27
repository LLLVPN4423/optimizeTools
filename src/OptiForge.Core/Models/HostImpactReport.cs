namespace OptiForge.Core.Models;

public sealed class HostImpactReport
{
    public List<string> Actions { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool LlamaStopped { get; set; }
    public int QosPoliciesRemoved { get; set; }
    public int StartupEntriesRestored { get; set; }
    public int TempFilesCleared { get; set; }
    public string Summary { get; set; } = "";
}

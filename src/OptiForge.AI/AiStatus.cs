namespace OptiForge.AI;

public sealed class AiStatus
{
    public bool RuntimePresent { get; init; }
    public bool ModelPresent { get; init; }
    public bool ServerReachable { get; init; }
    public string? ActiveModelPath { get; init; }
    public string Message { get; init; } = "";
}

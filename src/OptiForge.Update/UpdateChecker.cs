namespace OptiForge.Update;

/// <summary>
/// Phase 7: check GitHub Releases. Stub for skeleton.
/// </summary>
public sealed class UpdateChecker
{
    public string RepositoryUrl { get; } = "https://github.com/LLLVPN4423/optimizeTools";

    public Task<(bool updateAvailable, string message)> CheckAsync(CancellationToken ct = default)
    {
        // Offline-safe stub
        return Task.FromResult((false, "Update check sẽ nối GitHub Releases ở Phase 7. Repo: " + RepositoryUrl));
    }
}

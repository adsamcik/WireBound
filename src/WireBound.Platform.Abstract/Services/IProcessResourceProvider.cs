using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Platform-specific provider for per-process resource data (CPU + memory).
/// Returns raw snapshots — smoothing and CPU% calculation are handled by the service layer.
/// </summary>
public interface IProcessResourceProvider
{
    /// <summary>
    /// Get a snapshot of resource usage for all accessible processes.
    /// Some system processes may report 0 for memory/CPU if access is denied.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Resource data for each process</returns>
    Task<IReadOnlyList<ProcessResourceData>> GetProcessResourceDataAsync(
        CancellationToken cancellationToken = default);
}

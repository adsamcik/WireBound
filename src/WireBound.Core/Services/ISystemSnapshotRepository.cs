using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Repository for system snapshot data used for chart history.
/// </summary>
public interface ISystemSnapshotRepository
{
    /// <summary>
    /// Save a batch of system snapshots for chart history (more efficient than individual saves).
    /// </summary>
    Task SaveSystemSnapshotBatchAsync(IEnumerable<(double cpuPercent, double memoryPercent, DateTime timestamp)> snapshots);

    /// <summary>
    /// Get system stats history for a time range.
    /// </summary>
    Task<List<SystemSnapshot>> GetSystemHistoryAsync(DateTime since);

    /// <summary>
    /// Clean up old system snapshots beyond retention period.
    /// </summary>
    Task CleanupOldSystemSnapshotsAsync(TimeSpan maxAge);

    /// <summary>
    /// Persist a memory pressure event for historical analysis.
    /// </summary>
    Task SaveMemoryPressureEventAsync(MemoryPressureEvent pressureEvent);
}

using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Repository for speed snapshot data used for chart history.
/// </summary>
public interface ISpeedSnapshotRepository
{
    /// <summary>
    /// Save a speed snapshot for chart history.
    /// </summary>
    Task SaveSpeedSnapshotAsync(long downloadSpeedBps, long uploadSpeedBps);

    /// <summary>
    /// Save a batch of speed snapshots for chart history (more efficient than individual saves).
    /// </summary>
    Task SaveSpeedSnapshotBatchAsync(IEnumerable<(long downloadBps, long uploadBps, DateTime timestamp)> snapshots);

    /// <summary>
    /// Get speed history for a time range.
    /// </summary>
    Task<List<SpeedSnapshot>> GetSpeedHistoryAsync(DateTime since);

    /// <summary>
    /// Clean up old speed snapshots beyond retention period.
    /// </summary>
    Task CleanupOldSpeedSnapshotsAsync(TimeSpan maxAge);
}

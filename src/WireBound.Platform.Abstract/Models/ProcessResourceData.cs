namespace WireBound.Platform.Abstract.Models;

/// <summary>
/// Per-process resource data (CPU + memory) from platform providers.
/// CPU is reported as raw processor time ticks — the service layer
/// computes percentage from deltas between consecutive snapshots.
/// </summary>
public sealed class ProcessResourceData
{
    /// <summary>
    /// Process ID
    /// </summary>
    public int ProcessId { get; init; }

    /// <summary>
    /// Process name (e.g., "chrome")
    /// </summary>
    public string ProcessName { get; init; } = string.Empty;

    /// <summary>
    /// Full path to the executable, or empty if inaccessible
    /// </summary>
    public string ExecutablePath { get; init; } = string.Empty;

    /// <summary>
    /// Private (committed) memory in bytes — stable metric, not affected by OS paging
    /// </summary>
    public long PrivateBytes { get; init; }

    /// <summary>
    /// Working set (physical RAM) in bytes — secondary metric, fluctuates with OS paging
    /// </summary>
    public long WorkingSetBytes { get; init; }

    /// <summary>
    /// Total processor time in ticks (100-nanosecond intervals).
    /// Caller computes CPU% by diffing consecutive snapshots:
    /// cpuPercent = (ticksDelta / wallTimeDelta) / processorCount * 100
    /// </summary>
    public long CpuTimeTicks { get; init; }
}

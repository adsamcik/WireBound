using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Platform-specific provider for disk activity information (reads, writes, busy time).
/// </summary>
public interface IDiskInfoProvider
{
    /// <summary>
    /// Get current disk activity statistics aggregated across physical drives.
    /// </summary>
    DiskInfoData GetDiskInfo();

    /// <summary>
    /// Whether disk activity (busy %) monitoring is supported on this platform.
    /// </summary>
    bool SupportsActivityPercent { get; }
}

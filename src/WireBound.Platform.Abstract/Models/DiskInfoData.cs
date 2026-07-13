namespace WireBound.Platform.Abstract.Models;

/// <summary>
/// Disk activity data transfer object from platform providers.
/// Aggregates physical disk I/O across all drives.
/// </summary>
public sealed class DiskInfoData
{
    /// <summary>
    /// Disk read throughput in bytes per second
    /// </summary>
    public long ReadBytesPerSecond { get; init; }

    /// <summary>
    /// Disk write throughput in bytes per second
    /// </summary>
    public long WriteBytesPerSecond { get; init; }

    /// <summary>
    /// Disk activity percentage (0-100), i.e. the fraction of time the disk was busy.
    /// </summary>
    public double ActivityPercent { get; init; }
}

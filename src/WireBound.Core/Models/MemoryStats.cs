using WireBound.Core.Helpers;

namespace WireBound.Core.Models;

/// <summary>
/// Memory (RAM) statistics snapshot
/// </summary>
public class MemoryStats
{
    /// <summary>
    /// Timestamp when stats were captured
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// Total physical memory in bytes
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Used physical memory in bytes
    /// </summary>
    public long UsedBytes { get; set; }

    /// <summary>
    /// Available physical memory in bytes
    /// </summary>
    public long AvailableBytes { get; set; }

    /// <summary>
    /// Memory usage percentage (0-100)
    /// </summary>
    public double UsagePercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100 : 0;

    /// <summary>
    /// Total virtual memory (page file + physical) in bytes
    /// </summary>
    public long TotalVirtualBytes { get; set; }

    /// <summary>
    /// Used virtual memory in bytes
    /// </summary>
    public long UsedVirtualBytes { get; set; }

    /// <summary>
    /// Formatted total memory string (e.g., "16.0 GB")
    /// </summary>
    public string TotalFormatted => ByteFormatter.FormatBytes(TotalBytes);

    /// <summary>
    /// Formatted used memory string (e.g., "8.5 GB")
    /// </summary>
    public string UsedFormatted => ByteFormatter.FormatBytes(UsedBytes);

    /// <summary>
    /// Formatted available memory string (e.g., "7.5 GB")
    /// </summary>
    public string AvailableFormatted => ByteFormatter.FormatBytes(AvailableBytes);
}

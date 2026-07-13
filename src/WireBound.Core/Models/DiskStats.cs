using WireBound.Core.Helpers;

namespace WireBound.Core.Models;

/// <summary>
/// Disk activity statistics snapshot (read/write throughput and busy time).
/// </summary>
public class DiskStats
{
    /// <summary>
    /// Timestamp when stats were captured
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// Disk read throughput in bytes per second
    /// </summary>
    public long ReadBytesPerSecond { get; set; }

    /// <summary>
    /// Disk write throughput in bytes per second
    /// </summary>
    public long WriteBytesPerSecond { get; set; }

    /// <summary>
    /// Disk activity percentage (0-100), i.e. fraction of time the disk was busy.
    /// </summary>
    public double ActivityPercent { get; set; }

    /// <summary>
    /// Combined read + write throughput in bytes per second
    /// </summary>
    public long TotalBytesPerSecond => ReadBytesPerSecond + WriteBytesPerSecond;

    /// <summary>
    /// Formatted read speed string (e.g., "12.50 MB/s")
    /// </summary>
    public string ReadFormatted => ByteFormatter.FormatSpeedInBytes(ReadBytesPerSecond);

    /// <summary>
    /// Formatted write speed string (e.g., "3.20 MB/s")
    /// </summary>
    public string WriteFormatted => ByteFormatter.FormatSpeedInBytes(WriteBytesPerSecond);
}

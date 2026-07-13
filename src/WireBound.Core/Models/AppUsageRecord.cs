using System.ComponentModel.DataAnnotations;
using WireBound.Core.Helpers;

namespace WireBound.Core.Models;

/// <summary>
/// Granularity level for app usage records
/// </summary>
public enum UsageGranularity
{
    Hourly,
    Daily
}

/// <summary>
/// Represents aggregated network usage data for a specific application
/// </summary>
public class AppUsageRecord
{
    [Key]
    public int Id { get; set; }

    /// <summary>
    /// SHA256 hash of the executable path (stable identifier across sessions)
    /// </summary>
    public string AppIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// Friendly display name for the application
    /// </summary>
    public string AppName { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the executable
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Process name (for alternative grouping)
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp - represents the hour (for Hourly) or date (for Daily)
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Whether this record is hourly or daily granularity
    /// </summary>
    public UsageGranularity Granularity { get; set; }

    /// <summary>
    /// Total bytes received during this period
    /// </summary>
    public long BytesReceived { get; set; }

    /// <summary>
    /// Total bytes sent during this period
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// Subset of <see cref="BytesReceived"/> that was loopback / localhost
    /// traffic (127.0.0.0/8, ::1). Zero for data collected without the elevated
    /// helper, or before this field existed. Network bytes = total − loopback.
    /// </summary>
    public long LoopbackBytesReceived { get; set; }

    /// <summary>
    /// Subset of <see cref="BytesSent"/> that was loopback / localhost traffic.
    /// </summary>
    public long LoopbackBytesSent { get; set; }

    /// <summary>
    /// Peak download speed during this period (bytes/sec)
    /// </summary>
    public long PeakDownloadSpeed { get; set; }

    /// <summary>
    /// Peak upload speed during this period (bytes/sec)
    /// </summary>
    public long PeakUploadSpeed { get; set; }

    /// <summary>
    /// Last time this record was updated
    /// </summary>
    public DateTime LastUpdated { get; set; }

    /// <summary>
    /// Total bytes transferred (received + sent)
    /// </summary>
    public long TotalBytes => BytesReceived + BytesSent;

    /// <summary>
    /// Network (non-loopback) bytes received during this period.
    /// </summary>
    public long NetworkBytesReceived => System.Math.Max(0, BytesReceived - LoopbackBytesReceived);

    /// <summary>
    /// Network (non-loopback) bytes sent during this period.
    /// </summary>
    public long NetworkBytesSent => System.Math.Max(0, BytesSent - LoopbackBytesSent);

    /// <summary>
    /// Human-friendly download display (e.g. "1.4 MB"). Computed at bind time
    /// using <see cref="ByteFormatter"/> so the UI never has to wire a value
    /// converter just to render bytes.
    /// </summary>
    public string FormattedBytesReceived => ByteFormatter.FormatBytes(BytesReceived);

    /// <summary>
    /// Human-friendly upload display (e.g. "532 KB").
    /// </summary>
    public string FormattedBytesSent => ByteFormatter.FormatBytes(BytesSent);

    /// <summary>
    /// Human-friendly total display (e.g. "1.9 MB").
    /// </summary>
    public string FormattedTotalBytes => ByteFormatter.FormatBytes(TotalBytes);
}

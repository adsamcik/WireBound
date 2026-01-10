using System.ComponentModel.DataAnnotations;

namespace WireBound.Maui.Models;

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
    /// Peak download speed during this period (bytes/sec)
    /// </summary>
    public long PeakDownloadSpeed { get; set; }
    
    /// <summary>
    /// Peak upload speed during this period (bytes/sec)
    /// </summary>
    public long PeakUploadSpeed { get; set; }
    
    /// <summary>
    /// When this record was last updated
    /// </summary>
    public DateTime LastUpdated { get; set; }
    
    /// <summary>
    /// Combined bytes (received + sent)
    /// </summary>
    public long TotalBytes => BytesReceived + BytesSent;
}

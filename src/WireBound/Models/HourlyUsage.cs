using System.ComponentModel.DataAnnotations;

namespace WireBound.Models;

/// <summary>
/// Represents hourly aggregated network usage data for persistence
/// </summary>
public class HourlyUsage
{
    [Key]
    public int Id { get; set; }
    
    /// <summary>
    /// The hour this data represents (truncated to hour)
    /// </summary>
    public DateTime Hour { get; set; }
    
    /// <summary>
    /// Network adapter ID
    /// </summary>
    public string AdapterId { get; set; } = string.Empty;
    
    /// <summary>
    /// Bytes received during this hour
    /// </summary>
    public long BytesReceived { get; set; }
    
    /// <summary>
    /// Bytes sent during this hour
    /// </summary>
    public long BytesSent { get; set; }
    
    /// <summary>
    /// Peak download speed during this hour (bytes/sec)
    /// </summary>
    public long PeakDownloadSpeed { get; set; }
    
    /// <summary>
    /// Peak upload speed during this hour (bytes/sec)
    /// </summary>
    public long PeakUploadSpeed { get; set; }
    
    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime LastUpdated { get; set; }
}

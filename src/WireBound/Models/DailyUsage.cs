using System.ComponentModel.DataAnnotations;

namespace WireBound.Models;

/// <summary>
/// Represents daily aggregated network usage data
/// </summary>
public class DailyUsage
{
    [Key]
    public int Id { get; set; }
    
    /// <summary>
    /// The date this data represents
    /// </summary>
    public DateOnly Date { get; set; }
    
    /// <summary>
    /// Network adapter ID
    /// </summary>
    public string AdapterId { get; set; } = string.Empty;
    
    /// <summary>
    /// Total bytes received on this day
    /// </summary>
    public long BytesReceived { get; set; }
    
    /// <summary>
    /// Total bytes sent on this day
    /// </summary>
    public long BytesSent { get; set; }
    
    /// <summary>
    /// Peak download speed for the day (bytes/sec)
    /// </summary>
    public long PeakDownloadSpeed { get; set; }
    
    /// <summary>
    /// Peak upload speed for the day (bytes/sec)
    /// </summary>
    public long PeakUploadSpeed { get; set; }
    
    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime LastUpdated { get; set; }

    // Computed properties
    public long TotalBytes => BytesReceived + BytesSent;
}

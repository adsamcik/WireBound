using System.ComponentModel.DataAnnotations;

namespace WireBound.Models;

/// <summary>
/// Represents weekly aggregated network usage data
/// </summary>
public class WeeklyUsage
{
    [Key]
    public int Id { get; set; }
    
    /// <summary>
    /// The start date of the week (Monday)
    /// </summary>
    public DateOnly WeekStart { get; set; }
    
    /// <summary>
    /// Year and week number for easy querying
    /// </summary>
    public int Year { get; set; }
    
    /// <summary>
    /// ISO week number (1-53)
    /// </summary>
    public int WeekNumber { get; set; }
    
    /// <summary>
    /// Network adapter ID
    /// </summary>
    public string AdapterId { get; set; } = string.Empty;
    
    /// <summary>
    /// Total bytes received during this week
    /// </summary>
    public long BytesReceived { get; set; }
    
    /// <summary>
    /// Total bytes sent during this week
    /// </summary>
    public long BytesSent { get; set; }
    
    /// <summary>
    /// Peak download speed during this week (bytes/sec)
    /// </summary>
    public long PeakDownloadSpeed { get; set; }
    
    /// <summary>
    /// Peak upload speed during this week (bytes/sec)
    /// </summary>
    public long PeakUploadSpeed { get; set; }
    
    /// <summary>
    /// Number of active days in this week
    /// </summary>
    public int ActiveDays { get; set; }
    
    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime LastUpdated { get; set; }

    // Computed properties
    public long TotalBytes => BytesReceived + BytesSent;
    
    /// <summary>
    /// Average daily usage for this week
    /// </summary>
    public long AverageDailyBytes => ActiveDays > 0 ? TotalBytes / ActiveDays : 0;
}

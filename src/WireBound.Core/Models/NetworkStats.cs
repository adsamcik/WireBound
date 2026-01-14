using WireBound.Core.Helpers;

namespace WireBound.Core.Models;

/// <summary>
/// Real-time network statistics snapshot
/// </summary>
public class NetworkStats
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Current download speed in bytes per second
    /// </summary>
    public long DownloadSpeedBps { get; set; }
    
    /// <summary>
    /// Current upload speed in bytes per second
    /// </summary>
    public long UploadSpeedBps { get; set; }
    
    /// <summary>
    /// Total bytes received since monitoring started
    /// </summary>
    public long TotalBytesReceived { get; set; }
    
    /// <summary>
    /// Total bytes sent since monitoring started
    /// </summary>
    public long TotalBytesSent { get; set; }
    
    /// <summary>
    /// Session bytes received (since app started)
    /// </summary>
    public long SessionBytesReceived { get; set; }
    
    /// <summary>
    /// Session bytes sent (since app started)
    /// </summary>
    public long SessionBytesSent { get; set; }
    
    /// <summary>
    /// The adapter this data is from
    /// </summary>
    public string AdapterId { get; set; } = string.Empty;

    // Helper properties for formatted display
    public string DownloadSpeedFormatted => ByteFormatter.FormatSpeed(DownloadSpeedBps);
    public string UploadSpeedFormatted => ByteFormatter.FormatSpeed(UploadSpeedBps);
    public string SessionReceivedFormatted => ByteFormatter.FormatBytes(SessionBytesReceived);
    public string SessionSentFormatted => ByteFormatter.FormatBytes(SessionBytesSent);
}

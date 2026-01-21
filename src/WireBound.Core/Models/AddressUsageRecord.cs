using System.ComponentModel.DataAnnotations;

namespace WireBound.Core.Models;

/// <summary>
/// Aggregated network usage for a specific remote address.
/// Used for persistence and historical tracking.
/// </summary>
public class AddressUsageRecord
{
    [Key]
    public int Id { get; set; }
    
    /// <summary>
    /// Remote IP address
    /// </summary>
    public string RemoteAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// Resolved hostname (if available, cached)
    /// </summary>
    public string? Hostname { get; set; }
    
    /// <summary>
    /// Most common destination port (e.g., 443 for HTTPS, 80 for HTTP)
    /// </summary>
    public int PrimaryPort { get; set; }
    
    /// <summary>
    /// Protocol (TCP/UDP)
    /// </summary>
    public string Protocol { get; set; } = "TCP";
    
    /// <summary>
    /// Timestamp for this record (hour or day granularity)
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Granularity level (hourly or daily aggregation)
    /// </summary>
    public UsageGranularity Granularity { get; set; }
    
    /// <summary>
    /// Total bytes sent to this address during this period
    /// </summary>
    public long BytesSent { get; set; }
    
    /// <summary>
    /// Total bytes received from this address during this period
    /// </summary>
    public long BytesReceived { get; set; }
    
    /// <summary>
    /// Number of distinct connections to this address in this period
    /// </summary>
    public int ConnectionCount { get; set; }
    
    /// <summary>
    /// Peak send speed during this period (bytes/sec)
    /// </summary>
    public long PeakSendSpeed { get; set; }
    
    /// <summary>
    /// Peak receive speed during this period (bytes/sec)
    /// </summary>
    public long PeakReceiveSpeed { get; set; }
    
    /// <summary>
    /// Optional: Link to the process that generated this traffic
    /// Uses the stable AppIdentifier from ProcessNetworkStats
    /// </summary>
    public string? AppIdentifier { get; set; }
    
    /// <summary>
    /// Last time this record was updated
    /// </summary>
    public DateTime LastUpdated { get; set; }
    
    /// <summary>
    /// Total bytes transferred (sent + received)
    /// </summary>
    public long TotalBytes => BytesSent + BytesReceived;
    
    /// <summary>
    /// Display name (hostname if available, otherwise IP:port)
    /// </summary>
    public string DisplayName => Hostname ?? (PrimaryPort > 0 ? $"{RemoteAddress}:{PrimaryPort}" : RemoteAddress);
}

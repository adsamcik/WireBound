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
    
    // === VPN Traffic Analysis ===
    
    /// <summary>
    /// Whether a VPN adapter is connected (regardless of current traffic)
    /// </summary>
    public bool IsVpnConnected { get; set; }
    
    /// <summary>
    /// Whether VPN traffic analysis is active (VPN adapters detected with traffic)
    /// </summary>
    public bool HasVpnTraffic { get; set; }
    
    /// <summary>
    /// Download speed through VPN tunnel in bytes per second.
    /// This is the "actual" payload traffic flowing through the VPN.
    /// </summary>
    public long VpnDownloadSpeedBps { get; set; }
    
    /// <summary>
    /// Upload speed through VPN tunnel in bytes per second.
    /// This is the "actual" payload traffic flowing through the VPN.
    /// </summary>
    public long VpnUploadSpeedBps { get; set; }
    
    /// <summary>
    /// Download speed on physical adapters (total including VPN overhead).
    /// When VPN is active, this includes encrypted VPN packets.
    /// </summary>
    public long PhysicalDownloadSpeedBps { get; set; }
    
    /// <summary>
    /// Upload speed on physical adapters (total including VPN overhead).
    /// When VPN is active, this includes encrypted VPN packets.
    /// </summary>
    public long PhysicalUploadSpeedBps { get; set; }
    
    /// <summary>
    /// Estimated VPN overhead for download in bytes per second.
    /// Calculated as (Physical - VPN) when VPN is active.
    /// This includes encryption, encapsulation, and protocol overhead.
    /// </summary>
    public long VpnDownloadOverheadBps => HasVpnTraffic ? Math.Max(0, PhysicalDownloadSpeedBps - VpnDownloadSpeedBps) : 0;
    
    /// <summary>
    /// Estimated VPN overhead for upload in bytes per second.
    /// Calculated as (Physical - VPN) when VPN is active.
    /// </summary>
    public long VpnUploadOverheadBps => HasVpnTraffic ? Math.Max(0, PhysicalUploadSpeedBps - VpnUploadSpeedBps) : 0;
    
    /// <summary>
    /// Overhead percentage for download (0-100)
    /// </summary>
    public double VpnDownloadOverheadPercent => VpnDownloadSpeedBps > 0 
        ? Math.Round((double)VpnDownloadOverheadBps / VpnDownloadSpeedBps * 100, 1) 
        : 0;
    
    /// <summary>
    /// Overhead percentage for upload (0-100)
    /// </summary>
    public double VpnUploadOverheadPercent => VpnUploadSpeedBps > 0 
        ? Math.Round((double)VpnUploadOverheadBps / VpnUploadSpeedBps * 100, 1) 
        : 0;
    
    /// <summary>
    /// Session bytes received through VPN tunnel
    /// </summary>
    public long VpnSessionBytesReceived { get; set; }
    
    /// <summary>
    /// Session bytes sent through VPN tunnel
    /// </summary>
    public long VpnSessionBytesSent { get; set; }
    
    /// <summary>
    /// Names of connected VPN adapters (regardless of current traffic)
    /// </summary>
    public List<string> ConnectedVpnAdapters { get; set; } = [];
    
    /// <summary>
    /// Names of VPN adapters with active traffic
    /// </summary>
    public List<string> ActiveVpnAdapters { get; set; } = [];

    // Helper properties for formatted display
    public string DownloadSpeedFormatted => ByteFormatter.FormatSpeed(DownloadSpeedBps);
    public string UploadSpeedFormatted => ByteFormatter.FormatSpeed(UploadSpeedBps);
    public string SessionReceivedFormatted => ByteFormatter.FormatBytes(SessionBytesReceived);
    public string SessionSentFormatted => ByteFormatter.FormatBytes(SessionBytesSent);
    
    // VPN formatted display
    public string VpnDownloadSpeedFormatted => ByteFormatter.FormatSpeed(VpnDownloadSpeedBps);
    public string VpnUploadSpeedFormatted => ByteFormatter.FormatSpeed(VpnUploadSpeedBps);
    public string VpnDownloadOverheadFormatted => ByteFormatter.FormatSpeed(VpnDownloadOverheadBps);
    public string VpnUploadOverheadFormatted => ByteFormatter.FormatSpeed(VpnUploadOverheadBps);
}

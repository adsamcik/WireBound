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
    /// Typical VPN overhead percentage (encryption, encapsulation, headers).
    /// WireGuard: ~5-10%, OpenVPN: ~15-20%, IPSec: ~10-15%
    /// We use a conservative estimate since we can't detect the VPN type reliably.
    /// </summary>
    private const double EstimatedOverheadPercent = 0.10; // 10%
    
    /// <summary>
    /// Maximum reasonable overhead percentage before we assume split tunneling.
    /// If calculated overhead exceeds this, traffic is likely split-tunneled.
    /// </summary>
    private const double MaxReasonableOverheadPercent = 0.50; // 50%
    
    /// <summary>
    /// Whether split tunneling is likely active (physical traffic significantly exceeds VPN traffic).
    /// </summary>
    public bool IsSplitTunnelLikely => HasVpnTraffic && 
        (VpnDownloadSpeedBps > 0 && PhysicalDownloadSpeedBps > VpnDownloadSpeedBps * (1 + MaxReasonableOverheadPercent) ||
         VpnUploadSpeedBps > 0 && PhysicalUploadSpeedBps > VpnUploadSpeedBps * (1 + MaxReasonableOverheadPercent));
    
    /// <summary>
    /// Estimated VPN overhead for download in bytes per second.
    /// When split tunneling is detected, uses estimated overhead instead of calculated.
    /// </summary>
    public long VpnDownloadOverheadBps => HasVpnTraffic && VpnDownloadSpeedBps > 0
        ? (IsSplitTunnelLikely 
            ? (long)(VpnDownloadSpeedBps * EstimatedOverheadPercent)  // Estimated overhead
            : Math.Max(0, PhysicalDownloadSpeedBps - VpnDownloadSpeedBps))  // Calculated overhead
        : 0;
    
    /// <summary>
    /// Estimated VPN overhead for upload in bytes per second.
    /// When split tunneling is detected, uses estimated overhead instead of calculated.
    /// </summary>
    public long VpnUploadOverheadBps => HasVpnTraffic && VpnUploadSpeedBps > 0
        ? (IsSplitTunnelLikely 
            ? (long)(VpnUploadSpeedBps * EstimatedOverheadPercent)  // Estimated overhead
            : Math.Max(0, PhysicalUploadSpeedBps - VpnUploadSpeedBps))  // Calculated overhead
        : 0;
    
    /// <summary>
    /// Overhead percentage for download (0-100).
    /// Capped at reasonable maximum to avoid misleading values.
    /// </summary>
    public double VpnDownloadOverheadPercent => VpnDownloadSpeedBps > 0 
        ? Math.Min(Math.Round((double)VpnDownloadOverheadBps / VpnDownloadSpeedBps * 100, 1), MaxReasonableOverheadPercent * 100)
        : 0;
    
    /// <summary>
    /// Overhead percentage for upload (0-100).
    /// Capped at reasonable maximum to avoid misleading values.
    /// </summary>
    public double VpnUploadOverheadPercent => VpnUploadSpeedBps > 0 
        ? Math.Min(Math.Round((double)VpnUploadOverheadBps / VpnUploadSpeedBps * 100, 1), MaxReasonableOverheadPercent * 100)
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

using CommunityToolkit.Mvvm.ComponentModel;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Display model for adapters in the adapter dashboard, combining
/// NetworkAdapter info with WiFi info and current traffic stats.
/// </summary>
public partial class AdapterDisplayItem : ObservableObject
{
    public NetworkAdapter Adapter { get; }
    
    public string Id => Adapter.Id;
    public string Name => Adapter.Name;
    public string DisplayName => Adapter.DisplayName;
    public string Description => Adapter.Description;
    public NetworkAdapterType AdapterType => Adapter.AdapterType;
    public bool IsActive => Adapter.IsActive;
    public bool IsVirtual => Adapter.IsVirtual;
    public bool IsKnownVpn => Adapter.IsKnownVpn;
    public bool IsUsbTethering => Adapter.IsUsbTethering;
    public bool IsBluetoothTethering => Adapter.IsBluetoothTethering;
    public string Category => Adapter.Category;
    
    /// <summary>
    /// Icon/emoji for the adapter type
    /// </summary>
    public string TypeIcon => GetTypeIcon();
    
    /// <summary>
    /// Badge text (e.g., "VPN", "WiFi", "USB")
    /// </summary>
    public string? Badge => GetBadge();
    
    /// <summary>
    /// Badge background color
    /// </summary>
    public string BadgeColor => GetBadgeColor();
    
    /// <summary>
    /// WiFi info for wireless adapters
    /// </summary>
    [ObservableProperty]
    private WiFiInfo? _wiFiInfo;
    
    /// <summary>
    /// Whether this adapter has WiFi info available
    /// </summary>
    public bool HasWiFiInfo => !string.IsNullOrEmpty(WiFiInfo?.Ssid);
    
    /// <summary>
    /// WiFi signal bars (1-4) based on signal quality
    /// </summary>
    public int SignalBars => WiFiInfo?.SignalStrength switch
    {
        >= 75 => 4,
        >= 50 => 3,
        >= 25 => 2,
        > 0 => 1,
        _ => 0
    };
    
    /// <summary>
    /// WiFi signal icon based on strength
    /// </summary>
    public string WifiSignalIcon => SignalBars switch
    {
        4 => "📶",
        3 => "📶",
        2 => "📶",
        1 => "📶",
        _ => "📡"
    };
    
    /// <summary>
    /// Current download speed
    /// </summary>
    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";
    
    /// <summary>
    /// Current upload speed  
    /// </summary>
    [ObservableProperty]
    private string _uploadSpeed = "0 B/s";
    
    /// <summary>
    /// Session download total
    /// </summary>
    [ObservableProperty]
    private string _sessionDownload = "0 B";
    
    /// <summary>
    /// Session upload total
    /// </summary>
    [ObservableProperty]
    private string _sessionUpload = "0 B";
    
    /// <summary>
    /// Raw download bytes per second for sorting
    /// </summary>
    public long DownloadBps { get; private set; }
    
    /// <summary>
    /// Raw upload bytes per second for sorting
    /// </summary>
    public long UploadBps { get; private set; }
    
    /// <summary>
    /// Whether this adapter currently has traffic
    /// </summary>
    public bool HasTraffic => DownloadBps > 0 || UploadBps > 0;
    
    /// <summary>
    /// Status line (SSID for WiFi, description for others)
    /// </summary>
    public string StatusLine => GetStatusLine();
    
    public AdapterDisplayItem(NetworkAdapter adapter)
    {
        Adapter = adapter;
    }
    
    /// <summary>
    /// Update traffic stats from network stats
    /// </summary>
    public void UpdateTraffic(long downloadBps, long uploadBps, long sessionDownload, long sessionUpload)
    {
        DownloadBps = downloadBps;
        UploadBps = uploadBps;
        DownloadSpeed = ByteFormatter.FormatSpeed(downloadBps);
        UploadSpeed = ByteFormatter.FormatSpeed(uploadBps);
        SessionDownload = ByteFormatter.FormatBytes(sessionDownload);
        SessionUpload = ByteFormatter.FormatBytes(sessionUpload);
        OnPropertyChanged(nameof(HasTraffic));
    }
    
    private string GetTypeIcon()
    {
        if (IsKnownVpn) return "🔐";
        if (IsUsbTethering) return "📱";
        if (IsBluetoothTethering) return "🔗";
        
        return AdapterType switch
        {
            NetworkAdapterType.WiFi => "📶",
            NetworkAdapterType.Ethernet => "🔌",
            NetworkAdapterType.Loopback => "🔄",
            NetworkAdapterType.Tunnel => "🚇",
            _ => IsVirtual ? "💻" : "🌐"
        };
    }
    
    private string? GetBadge()
    {
        if (IsKnownVpn) return "VPN";
        if (IsUsbTethering) return "USB";
        if (IsBluetoothTethering) return "BT";
        if (AdapterType == NetworkAdapterType.WiFi && HasWiFiInfo) 
            return $"{WiFiInfo!.SignalStrength}%";
        if (IsVirtual) return "VM";
        return null;
    }
    
    private string GetBadgeColor()
    {
        if (IsKnownVpn) return "#A855F7"; // Purple for VPN
        if (IsUsbTethering || IsBluetoothTethering) return "#F59E0B"; // Amber for tethering
        if (AdapterType == NetworkAdapterType.WiFi) return "#22C55E"; // Green for WiFi
        if (IsVirtual) return "#6B7280"; // Gray for virtual
        return "#3B82F6"; // Blue default
    }
    
    private string GetStatusLine()
    {
        if (HasWiFiInfo && !string.IsNullOrEmpty(WiFiInfo!.Ssid))
            return $"{WiFiInfo.Ssid} • {WiFiInfo.Band ?? "WiFi"}";
        
        if (IsUsbTethering)
            return "USB Tethered Connection";
        
        if (IsBluetoothTethering)
            return "Bluetooth Tethered Connection";
        
        if (IsKnownVpn)
            return "Secure VPN Tunnel";
        
        if (!string.IsNullOrEmpty(Description))
            return Description.Length > 50 ? Description[..47] + "..." : Description;
        
        return IsActive ? "Connected" : "Disconnected";
    }
}

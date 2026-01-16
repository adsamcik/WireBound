using System.Net.NetworkInformation;
using ManagedNativeWifi;
using Microsoft.Extensions.Logging;
using WireBound.Core.Models;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Windows implementation using ManagedNativeWifi
/// </summary>
internal class WindowsWiFiInfoProvider : IWiFiInfoProvider
{
    private readonly ILogger _logger;
    
    public WindowsWiFiInfoProvider(ILogger logger)
    {
        _logger = logger;
    }
    
    public bool IsSupported => true;
    
    public WiFiInfo? GetWiFiInfo(string adapterId)
    {
        try
        {
            // Find the interface by matching adapter ID
            var interfaces = NativeWifi.EnumerateInterfaceConnections();
            
            foreach (var iface in interfaces)
            {
                // Try to match by interface GUID or description
                var guidString = iface.Id.ToString();
                
                // adapterId from NetworkInterface might contain the GUID
                if (adapterId.Contains(guidString, StringComparison.OrdinalIgnoreCase) ||
                    guidString.Contains(adapterId.Replace("{", "").Replace("}", ""), StringComparison.OrdinalIgnoreCase))
                {
                    return GetWiFiInfoForInterface(iface);
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get WiFi info for adapter {AdapterId}", adapterId);
            return null;
        }
    }
    
    public Dictionary<string, WiFiInfo> GetAllWiFiInfo()
    {
        var result = new Dictionary<string, WiFiInfo>();
        
        try
        {
            var interfaces = NativeWifi.EnumerateInterfaceConnections();
            
            foreach (var iface in interfaces)
            {
                if (!iface.IsConnected)
                    continue;
                    
                var info = GetWiFiInfoForInterface(iface);
                if (info != null)
                {
                    // Use interface GUID as key
                    result[iface.Id.ToString()] = info;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to enumerate WiFi interfaces");
        }
        
        return result;
    }
    
    private WiFiInfo? GetWiFiInfoForInterface(InterfaceConnectionInfo iface)
    {
        try
        {
            if (!iface.IsConnected)
                return null;
            
            // Get current connection info
            var (result, connection) = NativeWifi.GetCurrentConnection(iface.Id);
            if (result != ActionResult.Success || connection == null)
                return null;
            
            // Get signal strength (RSSI)
            int? rssi = null;
            var (rssiResult, rssiValue) = NativeWifi.GetRssi(iface.Id);
            if (rssiResult == ActionResult.Success)
            {
                rssi = rssiValue;
            }
            
            // Get quality and link speed from connection info
            int? quality = connection.SignalQuality;
            int? rxRate = connection.RxRate / 1000; // Convert Kbps to Mbps
            int? txRate = connection.TxRate / 1000;
            
            // Try to get channel/frequency from available networks
            int? channel = null;
            string? frequencyBand = null;
            
            // Try to find the BSS network for more detailed info
            try
            {
                var bssNetworks = NativeWifi.EnumerateBssNetworks()
                    .Where(n => n.InterfaceInfo.Id == iface.Id && n.Ssid.ToString() == connection.Ssid.ToString())
                    .FirstOrDefault();
                    
                if (bssNetworks != null)
                {
                    channel = bssNetworks.Channel;
                    frequencyBand = bssNetworks.Band > 0 ? $"{bssNetworks.Band} GHz" : null;
                }
            }
            catch
            {
                // BSS enumeration might fail, continue without channel info
            }
            
            return new WiFiInfo
            {
                Ssid = connection.Ssid.ToString(),
                SignalStrengthDbm = rssi,
                SignalQualityPercent = quality,
                LinkSpeedMbps = Math.Max(rxRate ?? 0, txRate ?? 0),
                Channel = channel,
                FrequencyBand = frequencyBand,
                SecurityType = connection.AuthenticationAlgorithm.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get WiFi info for interface {InterfaceId}", iface.Id);
            return null;
        }
    }
}

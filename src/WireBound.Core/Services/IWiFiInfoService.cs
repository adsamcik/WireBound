using WireBound.Platform.Abstract.Services;

namespace WireBound.Core.Services;

/// <summary>
/// Service for retrieving WiFi network information
/// </summary>
public interface IWiFiInfoService
{
    /// <summary>
    /// Get WiFi information for a specific network adapter
    /// </summary>
    /// <param name="adapterId">The adapter ID from NetworkInterface.Id</param>
    /// <returns>WiFi info if available, null if adapter is not WiFi or info unavailable</returns>
    WiFiInfo? GetWiFiInfo(string adapterId);
    
    /// <summary>
    /// Get WiFi information for all connected WiFi adapters
    /// </summary>
    /// <returns>Dictionary of adapter ID to WiFi info</returns>
    Dictionary<string, WiFiInfo> GetAllWiFiInfo();
    
    /// <summary>
    /// Check if the platform supports WiFi info retrieval
    /// </summary>
    bool IsSupported { get; }
}

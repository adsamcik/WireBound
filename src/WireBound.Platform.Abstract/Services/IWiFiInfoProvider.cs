namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Platform-specific WiFi information provider.
/// </summary>
public interface IWiFiInfoProvider
{
    /// <summary>
    /// Gets whether WiFi info is supported on this platform.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Gets WiFi information for a specific adapter.
    /// </summary>
    /// <param name="adapterId">The adapter ID to get WiFi info for.</param>
    /// <returns>WiFi information if available, null otherwise.</returns>
    WiFiInfo? GetWiFiInfo(string adapterId);

    /// <summary>
    /// Gets WiFi information for all connected adapters.
    /// </summary>
    /// <returns>Dictionary mapping adapter/interface names to their WiFi info.</returns>
    Dictionary<string, WiFiInfo> GetAllWiFiInfo();
}

/// <summary>
/// WiFi connection information.
/// </summary>
public sealed class WiFiInfo
{
    /// <summary>
    /// Network name (SSID).
    /// </summary>
    public required string Ssid { get; init; }

    /// <summary>
    /// Signal strength in percentage (0-100).
    /// </summary>
    public int SignalStrength { get; init; }

    /// <summary>
    /// Signal strength in dBm.
    /// </summary>
    public int? SignalDbm { get; init; }

    /// <summary>
    /// Link speed in Mbps.
    /// </summary>
    public int? LinkSpeedMbps { get; init; }

    /// <summary>
    /// WiFi frequency in MHz.
    /// </summary>
    public int? FrequencyMhz { get; init; }

    /// <summary>
    /// WiFi channel number.
    /// </summary>
    public int? Channel { get; init; }

    /// <summary>
    /// Security type (e.g., WPA2, WPA3).
    /// </summary>
    public string? Security { get; init; }

    /// <summary>
    /// BSSID of the access point.
    /// </summary>
    public string? Bssid { get; init; }

    /// <summary>
    /// WiFi band (2.4 GHz or 5 GHz).
    /// </summary>
    public string? Band { get; init; }

    /// <summary>
    /// PHY type (e.g., 802.11ac, 802.11ax).
    /// </summary>
    public string? PhyType { get; init; }
}

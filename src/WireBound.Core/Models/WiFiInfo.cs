namespace WireBound.Core.Models;

/// <summary>
/// WiFi connection information for an adapter
/// </summary>
public class WiFiInfo
{
    /// <summary>
    /// The network SSID (name)
    /// </summary>
    public string? Ssid { get; set; }
    
    /// <summary>
    /// Signal strength in dBm (typically -30 to -90)
    /// </summary>
    public int? SignalStrengthDbm { get; set; }
    
    /// <summary>
    /// Signal quality as percentage (0-100)
    /// </summary>
    public int? SignalQualityPercent { get; set; }
    
    /// <summary>
    /// Link speed in Mbps
    /// </summary>
    public int? LinkSpeedMbps { get; set; }
    
    /// <summary>
    /// Security type (e.g., WPA2, WPA3)
    /// </summary>
    public string? SecurityType { get; set; }
    
    /// <summary>
    /// WiFi channel number
    /// </summary>
    public int? Channel { get; set; }
    
    /// <summary>
    /// WiFi frequency band (e.g., "2.4 GHz", "5 GHz", "6 GHz")
    /// </summary>
    public string? FrequencyBand { get; set; }
    
    /// <summary>
    /// Whether WiFi info was successfully retrieved
    /// </summary>
    public bool IsAvailable => !string.IsNullOrEmpty(Ssid);
    
    /// <summary>
    /// Get signal quality description based on dBm or percentage
    /// </summary>
    public string SignalDescription
    {
        get
        {
            var quality = SignalQualityPercent ?? ConvertDbmToPercent(SignalStrengthDbm);
            if (quality == null) return "Unknown";
            
            return quality switch
            {
                >= 80 => "Excellent",
                >= 60 => "Good",
                >= 40 => "Fair",
                >= 20 => "Weak",
                _ => "Poor"
            };
        }
    }
    
    /// <summary>
    /// Convert dBm to approximate percentage
    /// </summary>
    private static int? ConvertDbmToPercent(int? dbm)
    {
        if (dbm == null) return null;
        
        // Typical range: -30 dBm (excellent) to -90 dBm (poor)
        var clamped = Math.Clamp(dbm.Value, -90, -30);
        return (int)((clamped + 90) * 100.0 / 60.0);
    }
}

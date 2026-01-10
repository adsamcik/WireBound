namespace WireBound.Maui.Models;

/// <summary>
/// Represents a network adapter/interface on the system
/// </summary>
public class NetworkAdapter
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public NetworkAdapterType AdapterType { get; set; }
    public bool IsActive { get; set; }
    public long Speed { get; set; } // bits per second
}

public enum NetworkAdapterType
{
    Unknown,
    Ethernet,
    WiFi,
    Loopback,
    Tunnel,
    Other
}

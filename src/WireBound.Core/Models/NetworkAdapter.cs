namespace WireBound.Core.Models;

/// <summary>
/// Represents a network adapter/interface on the system
/// </summary>
public class NetworkAdapter
{
    public string Id { get; set; } = string.Empty;
    
    /// <summary>
    /// System name of the adapter (e.g., "wt0", "Ethernet")
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// Friendly display name including VPN provider if detected (e.g., "wt0 (WireGuard)")
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;
    
    public string Description { get; set; } = string.Empty;
    public NetworkAdapterType AdapterType { get; set; }
    public bool IsActive { get; set; }
    public long Speed { get; set; } // bits per second
    
    /// <summary>
    /// True for virtual adapters (Hyper-V, VMware, containers, etc.)
    /// These are hidden in simple mode but visible in advanced mode.
    /// Note: Known VPNs are NOT considered virtual for display purposes.
    /// </summary>
    public bool IsVirtual { get; set; }
    
    /// <summary>
    /// True if this is a recognized VPN adapter.
    /// VPNs are shown in simple mode unlike other virtual adapters.
    /// </summary>
    public bool IsKnownVpn { get; set; }
    
    /// <summary>
    /// Category for display grouping (e.g., "VPN", "Virtual Machine", "Physical")
    /// </summary>
    public string Category { get; set; } = "Physical";
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

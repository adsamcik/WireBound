using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Cross-platform network monitoring service using System.Net.NetworkInformation
/// </summary>
public sealed class CrossPlatformNetworkMonitorService : INetworkMonitorService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AdapterState> _adapterStates = new();
    private readonly ILogger<CrossPlatformNetworkMonitorService> _logger;
    private readonly Stopwatch _pollStopwatch = Stopwatch.StartNew();
    private string _selectedAdapterId = string.Empty;
    private NetworkStats _currentStats = new();

    public event EventHandler<NetworkStats>? StatsUpdated;

    // IP Helper API is Windows-only, not available in cross-platform version
    public bool IsUsingIpHelperApi => false;

    public CrossPlatformNetworkMonitorService(ILogger<CrossPlatformNetworkMonitorService> logger)
    {
        _logger = logger;
        InitializeAdapters();
    }

    private void InitializeAdapters()
    {
        lock (_lock)
        {
            _adapterStates.Clear();
            
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up && 
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    !IsFilterOrSystemAdapter(nic))
                {
                    try
                    {
                        // Use GetIPStatistics() to include both IPv4 and IPv6 traffic
                        var stats = nic.GetIPStatistics();
                        var isVirtual = IsVirtualAdapter(nic);
                        
                        _adapterStates[nic.Id] = new AdapterState
                        {
                            Adapter = MapAdapter(nic, isVirtual),
                            LastBytesReceived = stats.BytesReceived,
                            LastBytesSent = stats.BytesSent,
                            SessionStartReceived = stats.BytesReceived,
                            SessionStartSent = stats.BytesSent,
                            LastPollTimestampMs = _pollStopwatch.ElapsedMilliseconds,
                            IsVirtual = isVirtual
                        };

                        _logger.LogInformation("Discovered network adapter: {AdapterName} ({AdapterId}) Virtual={IsVirtual} Category={Category}",
                            nic.Name, nic.Id, isVirtual, GetAdapterCategory(nic));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to initialize adapter {AdapterName}", nic.Name);
                    }
                }
            }

            _logger.LogInformation("Initialized {AdapterCount} network adapters", _adapterStates.Count);
        }
    }

    /// <summary>
    /// Filters out Windows Filtering Platform (WFP) components, packet schedulers, 
    /// and other low-level system adapters that aren't real network interfaces.
    /// These clutter the UI and shouldn't be shown to users.
    /// </summary>
    private static bool IsFilterOrSystemAdapter(NetworkInterface nic)
    {
        var name = nic.Name;
        
        // All WFP filters, QoS schedulers, and extension filters end with -0000
        // Examples: "Ethernet-WFP 802.3 MAC Layer LightWeight Filter-0000"
        //           "Ethernet-QoS Packet Scheduler-0000"
        //           "vSwitch (Default Switch)-Hyper-V Virtual Switch Extension Filter-0000"
        if (name.EndsWith("-0000"))
            return true;
        
        // Local Area Connection* numbered adapters (bridge/WFP components)
        if (name.StartsWith("Local Area Connection*"))
            return true;
        
        return false;
    }

    /// <summary>
    /// Detects if an adapter is a known VPN and returns the provider name.
    /// Returns null if not a recognized VPN.
    /// </summary>
    private static string? DetectVpnProvider(NetworkInterface nic)
    {
        var name = nic.Name.ToLowerInvariant();
        var description = nic.Description.ToLowerInvariant();
        var type = nic.NetworkInterfaceType;
        
        // WireGuard (generic)
        if (name.StartsWith("wg") || name.StartsWith("wt") || description.Contains("wireguard"))
            return "WireGuard";
        
        // NordVPN (NordLynx is WireGuard-based)
        if (description.Contains("nordvpn") || description.Contains("nordlynx"))
            return "NordVPN";
        
        // ExpressVPN (Lightway protocol)
        if (description.Contains("expressvpn") || description.Contains("lightway"))
            return "ExpressVPN";
        
        // Surfshark
        if (description.Contains("surfshark"))
            return "Surfshark";
        
        // ProtonVPN
        if (description.Contains("protonvpn") || description.Contains("proton vpn"))
            return "ProtonVPN";
        
        // Private Internet Access (PIA)
        if (description.Contains("private internet access"))
            return "Private Internet Access";
        
        // CyberGhost
        if (description.Contains("cyberghost"))
            return "CyberGhost";
        
        // Mullvad
        if (description.Contains("mullvad"))
            return "Mullvad";
        
        // Cloudflare WARP
        if (description.Contains("cloudflare") || description.Contains("warp"))
            return "Cloudflare WARP";
        
        // Tailscale (mesh VPN)
        if (description.Contains("tailscale"))
            return "Tailscale";
        
        // ZeroTier (mesh VPN)
        if (description.Contains("zerotier"))
            return "ZeroTier";
        
        // OpenVPN
        if (description.Contains("openvpn") || description.Contains("tap-windows") ||
            name.StartsWith("tap-") || name.StartsWith("tun"))
            return "OpenVPN";
        
        // Cisco AnyConnect
        if (description.Contains("cisco") || description.Contains("anyconnect"))
            return "Cisco AnyConnect";
        
        // GlobalProtect (Palo Alto)
        if (description.Contains("globalprotect") || description.Contains("palo alto"))
            return "GlobalProtect";
        
        // Fortinet/FortiClient
        if (description.Contains("fortinet") || description.Contains("forticlient"))
            return "FortiClient";
        
        // Pulse Secure / Ivanti
        if (description.Contains("pulse secure") || description.Contains("ivanti"))
            return "Pulse Secure";
        
        // Juniper VPN
        if (description.Contains("juniper"))
            return "Juniper VPN";
        
        // SoftEther VPN
        if (description.Contains("softether"))
            return "SoftEther";
        
        // Hamachi (LogMeIn)
        if (description.Contains("hamachi") || description.Contains("logmein"))
            return "Hamachi";
        
        // IPVanish
        if (description.Contains("ipvanish"))
            return "IPVanish";
        
        // HideMyAss (HMA)
        if (description.Contains("hidemyass") || description.Contains("hma"))
            return "HideMyAss";
        
        // Windscribe
        if (description.Contains("windscribe"))
            return "Windscribe";
        
        // TunnelBear
        if (description.Contains("tunnelbear"))
            return "TunnelBear";
        
        // Hotspot Shield
        if (description.Contains("hotspot shield") || description.Contains("anchorfree"))
            return "Hotspot Shield";
        
        // Generic VPN/Tunnel detection
        if (type == NetworkInterfaceType.Tunnel || type == NetworkInterfaceType.Ppp)
            return "VPN";
        
        if (description.Contains("vpn adapter") || description.Contains("tunnel adapter"))
            return "VPN";
        
        return null;
    }

    /// <summary>
    /// Determines if an adapter is a virtual machine or container adapter (not VPN).
    /// These are hidden in simple mode.
    /// </summary>
    private static bool IsVmOrContainerAdapter(NetworkInterface nic)
    {
        var name = nic.Name.ToLowerInvariant();
        var description = nic.Description.ToLowerInvariant();
        
        // Hyper-V virtual adapters
        if (name.StartsWith("vethernet") || name.StartsWith("vswitch") ||
            description.Contains("hyper-v"))
            return true;
        
        // VMware adapters
        if (description.Contains("vmware") || description.Contains("vmnet"))
            return true;
        
        // VirtualBox adapters
        if (description.Contains("virtualbox") || description.Contains("vbox"))
            return true;
        
        // Parallels adapters (macOS)
        if (description.Contains("parallels"))
            return true;
        
        // WSL adapters
        if (name.Contains("wsl") || description.Contains("wsl"))
            return true;
        
        // Docker/Container adapters
        if (name.Contains("docker") || description.Contains("docker") ||
            name.Contains("podman") || description.Contains("podman"))
            return true;
        
        // QEMU/KVM
        if (description.Contains("qemu") || description.Contains("virtio"))
            return true;
        
        return false;
    }

    /// <summary>
    /// Determines if an adapter is virtual (VM, container, or VPN).
    /// VPNs are marked separately via IsKnownVpn for display filtering.
    /// </summary>
    private static bool IsVirtualAdapter(NetworkInterface nic)
    {
        // VPNs are "virtual" for traffic aggregation purposes
        if (DetectVpnProvider(nic) != null)
            return true;
        
        // VM and container adapters
        if (IsVmOrContainerAdapter(nic))
            return true;
        
        return false;
    }
    
    /// <summary>
    /// Gets the category for display grouping in the UI
    /// </summary>
    private static string GetAdapterCategory(NetworkInterface nic)
    {
        // Check for VPN first
        if (DetectVpnProvider(nic) != null)
            return "VPN";
        
        var name = nic.Name.ToLowerInvariant();
        var description = nic.Description.ToLowerInvariant();
        
        // Virtual Machine detection
        if (name.StartsWith("vethernet") || name.StartsWith("vswitch") ||
            description.Contains("hyper-v") || description.Contains("vmware") ||
            description.Contains("virtualbox") || description.Contains("parallels") ||
            description.Contains("qemu") || description.Contains("virtio"))
            return "Virtual Machine";
        
        // Container detection
        if (name.Contains("docker") || description.Contains("docker") ||
            name.Contains("wsl") || description.Contains("wsl") ||
            name.Contains("podman"))
            return "Container";
        
        // Physical adapter
        return "Physical";
    }
    
    /// <summary>
    /// Generates a friendly display name for the adapter.
    /// For VPNs, includes the provider name. For VMs, includes the VM type.
    /// </summary>
    private static string GetDisplayName(NetworkInterface nic)
    {
        var name = nic.Name;
        var description = nic.Description.ToLowerInvariant();
        
        // Check for VPN provider
        var vpnProvider = DetectVpnProvider(nic);
        if (vpnProvider != null)
            return $"{name} ({vpnProvider})";
        
        // Check for VM type
        if (description.Contains("hyper-v"))
            return $"{name} (Hyper-V)";
        if (description.Contains("vmware"))
            return $"{name} (VMware)";
        if (description.Contains("virtualbox") || description.Contains("vbox"))
            return $"{name} (VirtualBox)";
        if (description.Contains("parallels"))
            return $"{name} (Parallels)";
        if (description.Contains("qemu") || description.Contains("virtio"))
            return $"{name} (QEMU/KVM)";
        
        // Check for container
        if (description.Contains("docker") || nic.Name.ToLowerInvariant().Contains("docker"))
            return $"{name} (Docker)";
        if (description.Contains("wsl") || nic.Name.ToLowerInvariant().Contains("wsl"))
            return $"{name} (WSL)";
        if (description.Contains("podman") || nic.Name.ToLowerInvariant().Contains("podman"))
            return $"{name} (Podman)";
        
        // Physical adapter - just the name
        return name;
    }

    private static NetworkAdapter MapAdapter(NetworkInterface nic, bool isVirtual)
    {
        var vpnProvider = DetectVpnProvider(nic);
        
        return new NetworkAdapter
        {
            Id = nic.Id,
            Name = nic.Name,
            DisplayName = GetDisplayName(nic),
            Description = nic.Description,
            AdapterType = MapAdapterType(nic.NetworkInterfaceType),
            Speed = nic.Speed,
            IsActive = nic.OperationalStatus == OperationalStatus.Up,
            IsVirtual = isVirtual,
            IsKnownVpn = vpnProvider != null,
            Category = GetAdapterCategory(nic)
        };
    }

    private static NetworkAdapterType MapAdapterType(NetworkInterfaceType type) => type switch
    {
        NetworkInterfaceType.Ethernet => NetworkAdapterType.Ethernet,
        NetworkInterfaceType.Wireless80211 => NetworkAdapterType.WiFi,
        NetworkInterfaceType.Loopback => NetworkAdapterType.Loopback,
        NetworkInterfaceType.Tunnel => NetworkAdapterType.Tunnel,
        _ => NetworkAdapterType.Other
    };

    public IReadOnlyList<NetworkAdapter> GetAdapters(bool includeVirtual = false)
    {
        lock (_lock)
        {
            var adapters = _adapterStates.Values.Select(s => s.Adapter);
            
            if (!includeVirtual)
            {
                // Simple mode: show physical adapters AND known VPNs
                adapters = adapters.Where(a => !a.IsVirtual || a.IsKnownVpn);
            }
            
            return adapters.ToList();
        }
    }

    public NetworkStats GetCurrentStats() => _currentStats;

    public NetworkStats GetStats(string adapterId)
    {
        lock (_lock)
        {
            if (_adapterStates.TryGetValue(adapterId, out var state))
            {
                return CreateStats(state);
            }
            return new NetworkStats();
        }
    }

    public void SetAdapter(string adapterId)
    {
        lock (_lock)
        {
            _selectedAdapterId = adapterId;
        }
    }

    public void SetUseIpHelperApi(bool useIpHelper)
    {
        // IP Helper API is Windows-only, ignore in cross-platform version
        _logger.LogDebug("IP Helper API is not available in cross-platform version");
    }

    public void ResetSession()
    {
        lock (_lock)
        {
            var nowMs = _pollStopwatch.ElapsedMilliseconds;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!_adapterStates.TryGetValue(nic.Id, out var state))
                    continue;

                try
                {
                    var stats = nic.GetIPStatistics();
                    state.SessionStartReceived = stats.BytesReceived;
                    state.SessionStartSent = stats.BytesSent;
                    state.LastPollTimestampMs = nowMs;
                }
                catch (NetworkInformationException)
                {
                    // Adapter might have been disconnected
                }
            }

            _currentStats = new NetworkStats
            {
                Timestamp = DateTime.Now,
                DownloadSpeedBps = 0,
                UploadSpeedBps = 0,
                SessionBytesReceived = 0,
                SessionBytesSent = 0,
                AdapterId = _selectedAdapterId
            };

            _logger.LogInformation("Network session reset");
        }
    }

    public void Poll()
    {
        lock (_lock)
        {
            var nowMs = _pollStopwatch.ElapsedMilliseconds;
            long totalDownloadSpeed = 0;
            long totalUploadSpeed = 0;
            long totalSessionReceived = 0;
            long totalSessionSent = 0;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!_adapterStates.TryGetValue(nic.Id, out var state))
                    continue;

                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                try
                {
                    // Use GetIPStatistics() which includes both IPv4 and IPv6 traffic
                    // This matches what Task Manager shows
                    var stats = nic.GetIPStatistics();
                    var elapsedMs = nowMs - state.LastPollTimestampMs;
                    
                    // Only calculate if we have meaningful elapsed time (at least 100ms)
                    if (elapsedMs >= 100)
                    {
                        var bytesReceivedDelta = stats.BytesReceived - state.LastBytesReceived;
                        var bytesSentDelta = stats.BytesSent - state.LastBytesSent;

                        // Handle counter reset (system reboot)
                        if (bytesReceivedDelta < 0) bytesReceivedDelta = stats.BytesReceived;
                        if (bytesSentDelta < 0) bytesSentDelta = stats.BytesSent;

                        // Calculate bytes per second: (bytes * 1000) / elapsedMs
                        state.DownloadSpeedBps = bytesReceivedDelta * 1000 / elapsedMs;
                        state.UploadSpeedBps = bytesSentDelta * 1000 / elapsedMs;
                        
                        // Debug logging to diagnose speed calculation
                        if (state.DownloadSpeedBps > 100000 || state.UploadSpeedBps > 100000) // Only log significant traffic
                        {
                            _logger.LogDebug(
                                "Adapter {Name}: elapsed={ElapsedMs}ms, rxDelta={RxDelta}, txDelta={TxDelta}, rxSpeed={RxSpeed} B/s, txSpeed={TxSpeed} B/s",
                                state.Adapter.Name, elapsedMs, bytesReceivedDelta, bytesSentDelta, 
                                state.DownloadSpeedBps, state.UploadSpeedBps);
                        }
                        
                        state.LastBytesReceived = stats.BytesReceived;
                        state.LastBytesSent = stats.BytesSent;
                        state.LastPollTimestampMs = nowMs;

                        // Aggregate based on adapter selection:
                        // - If specific adapter selected: only include that adapter
                        // - If "All Adapters" (empty): only include physical adapters to avoid double-counting
                        bool includeInAggregate = !string.IsNullOrEmpty(_selectedAdapterId)
                            ? _selectedAdapterId == nic.Id
                            : !state.IsVirtual; // Exclude virtual adapters from "All Adapters" aggregate
                        
                        if (includeInAggregate)
                        {
                            totalDownloadSpeed += state.DownloadSpeedBps;
                            totalUploadSpeed += state.UploadSpeedBps;
                            totalSessionReceived += stats.BytesReceived - state.SessionStartReceived;
                            totalSessionSent += stats.BytesSent - state.SessionStartSent;
                        }
                    }
                }
                catch (NetworkInformationException ex)
                {
                    // Adapter might have been disconnected
                    _logger.LogWarning(ex, "Error reading stats for adapter {AdapterId}", nic.Id);
                }
            }

            _currentStats = new NetworkStats
            {
                Timestamp = DateTime.Now,
                DownloadSpeedBps = totalDownloadSpeed,
                UploadSpeedBps = totalUploadSpeed,
                SessionBytesReceived = totalSessionReceived,
                SessionBytesSent = totalSessionSent,
                AdapterId = _selectedAdapterId
            };

            StatsUpdated?.Invoke(this, _currentStats);
        }
    }

    private NetworkStats CreateStats(AdapterState state)
    {
        return new NetworkStats
        {
            Timestamp = DateTime.Now,
            DownloadSpeedBps = state.DownloadSpeedBps,
            UploadSpeedBps = state.UploadSpeedBps,
            SessionBytesReceived = state.LastBytesReceived - state.SessionStartReceived,
            SessionBytesSent = state.LastBytesSent - state.SessionStartSent,
            AdapterId = state.Adapter.Id
        };
    }

    /// <summary>
    /// Internal state for tracking adapter statistics
    /// </summary>
    private class AdapterState
    {
        public NetworkAdapter Adapter { get; set; } = new();
        public long LastBytesReceived { get; set; }
        public long LastBytesSent { get; set; }
        public long SessionStartReceived { get; set; }
        public long SessionStartSent { get; set; }
        public long LastPollTimestampMs { get; set; }
        public long DownloadSpeedBps { get; set; }
        public long UploadSpeedBps { get; set; }
        /// <summary>
        /// Virtual adapters (Hyper-V, VMware, VPN tunnels) are excluded from "All Adapters"
        /// aggregate to avoid double-counting traffic that passes through multiple interfaces.
        /// </summary>
        public bool IsVirtual { get; set; }
    }
}

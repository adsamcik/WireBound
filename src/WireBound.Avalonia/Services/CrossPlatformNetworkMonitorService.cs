using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
    private string _selectedAdapterId = NetworkMonitorConstants.AutoAdapterId;
    private string _resolvedPrimaryAdapterId = string.Empty;
    private string _resolvedPrimaryAdapterName = string.Empty;
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

        // Parallels adapters
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

        // Check for tethering
        var (isUsb, isBluetooth) = DetectTethering(nic);
        if (isUsb)
        {
            if (description.Contains("apple"))
                return $"{name} (iPhone USB)";
            return $"{name} (USB Tethering)";
        }
        if (isBluetooth)
            return $"{name} (Bluetooth)";

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
        var (isUsbTethering, isBluetoothTethering) = DetectTethering(nic);

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
            IsUsbTethering = isUsbTethering,
            IsBluetoothTethering = isBluetoothTethering,
            Category = GetAdapterCategory(nic)
        };
    }

    /// <summary>
    /// Detects if the adapter is a USB or Bluetooth tethering connection
    /// </summary>
    private static (bool IsUsb, bool IsBluetooth) DetectTethering(NetworkInterface nic)
    {
        var description = nic.Description.ToLowerInvariant();
        var name = nic.Name.ToLowerInvariant();

        // USB Tethering detection
        // Android: "Remote NDIS based Internet Sharing Device" or "RNDIS"
        // iPhone: "Apple Mobile Device Ethernet"
        bool isUsb = description.Contains("rndis") ||
                     description.Contains("remote ndis") ||
                     description.Contains("apple mobile device ethernet") ||
                     description.Contains("android usb ethernet") ||
                     name.Contains("usb") && (name.Contains("eth") || name.Contains("net"));

        // Bluetooth tethering detection
        // Look for Bluetooth PAN (Personal Area Network) adapters
        bool isBluetooth = description.Contains("bluetooth") &&
                           (description.Contains("network") ||
                            description.Contains("pan") ||
                            nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet) ||
                           name.Contains("bnep"); // Linux Bluetooth network interface

        return (isUsb, isBluetooth);
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

    public IReadOnlyDictionary<string, NetworkStats> GetAllAdapterStats()
    {
        lock (_lock)
        {
            var result = new Dictionary<string, NetworkStats>();
            foreach (var kvp in _adapterStates)
            {
                if (kvp.Value.Adapter.IsActive)
                {
                    result[kvp.Key] = CreateStats(kvp.Value);
                }
            }
            return result;
        }
    }

    public void SetAdapter(string adapterId)
    {
        lock (_lock)
        {
            _selectedAdapterId = adapterId;
        }
    }

    public string GetPrimaryAdapterId()
    {
        lock (_lock)
        {
            return DetectGatewayAdapterId();
        }
    }

    /// <summary>
    /// Detects the primary internet adapter by finding the adapter with a default gateway.
    /// Prefers adapters with IPv4 gateways, falls back to IPv6.
    /// </summary>
    private string DetectGatewayAdapterId()
    {
        string? bestAdapterId = null;
        bool bestHasIpv4Gateway = false;

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;

            if (!_adapterStates.ContainsKey(nic.Id))
                continue;

            try
            {
                var ipProps = nic.GetIPProperties();
                var gateways = ipProps.GatewayAddresses;

                if (gateways.Count == 0)
                    continue;

                // Check for real gateways (not 0.0.0.0 or ::)
                bool hasIpv4Gateway = gateways.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !g.Address.Equals(IPAddress.Any));

                bool hasIpv6Gateway = gateways.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetworkV6 &&
                    !g.Address.Equals(IPAddress.IPv6Any));

                if (!hasIpv4Gateway && !hasIpv6Gateway)
                    continue;

                // Prefer IPv4 gateway adapters over IPv6-only
                if (bestAdapterId == null || (hasIpv4Gateway && !bestHasIpv4Gateway))
                {
                    bestAdapterId = nic.Id;
                    bestHasIpv4Gateway = hasIpv4Gateway;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check gateway for adapter {AdapterId}", nic.Id);
            }
        }

        return bestAdapterId ?? string.Empty;
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

            // Physical adapter totals
            long physicalDownloadSpeed = 0;
            long physicalUploadSpeed = 0;
            long physicalSessionReceived = 0;
            long physicalSessionSent = 0;

            // VPN adapter totals (for overhead calculation)
            long vpnDownloadSpeed = 0;
            long vpnUploadSpeed = 0;
            long vpnSessionReceived = 0;
            long vpnSessionSent = 0;

            // Selected adapter totals (when specific adapter is selected)
            long selectedDownloadSpeed = 0;
            long selectedUploadSpeed = 0;
            long selectedSessionReceived = 0;
            long selectedSessionSent = 0;

            // Track active VPN adapters
            var activeVpnAdapters = new List<string>();
            var connectedVpnAdapters = new List<string>();
            bool isAutoMode = _selectedAdapterId == NetworkMonitorConstants.AutoAdapterId;
            string resolvedAutoAdapterId = isAutoMode ? DetectGatewayAdapterId() : string.Empty;
            string resolvedAutoAdapterName = string.Empty;
            bool hasSelectedAdapter = !string.IsNullOrEmpty(_selectedAdapterId) && !isAutoMode;

            // In auto mode, the "selected" adapter is the gateway adapter
            if (isAutoMode && !string.IsNullOrEmpty(resolvedAutoAdapterId))
            {
                hasSelectedAdapter = true;
                if (_adapterStates.TryGetValue(resolvedAutoAdapterId, out var autoState))
                {
                    resolvedAutoAdapterName = autoState.Adapter.DisplayName;
                }
            }

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!_adapterStates.TryGetValue(nic.Id, out var state))
                    continue;

                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                // Track connected VPN adapters (regardless of traffic)
                if (state.Adapter.IsKnownVpn)
                {
                    connectedVpnAdapters.Add(state.Adapter.DisplayName);
                }

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

                        var sessionReceived = stats.BytesReceived - state.SessionStartReceived;
                        var sessionSent = stats.BytesSent - state.SessionStartSent;

                        // If a specific adapter is selected (or auto-resolved), track it
                        var effectiveSelectedId = isAutoMode ? resolvedAutoAdapterId : _selectedAdapterId;
                        if (hasSelectedAdapter && effectiveSelectedId == nic.Id)
                        {
                            selectedDownloadSpeed = state.DownloadSpeedBps;
                            selectedUploadSpeed = state.UploadSpeedBps;
                            selectedSessionReceived = sessionReceived;
                            selectedSessionSent = sessionSent;
                        }

                        // Always categorize traffic for VPN analysis
                        if (state.Adapter.IsKnownVpn)
                        {
                            // This is VPN tunnel traffic (the "actual" user payload)
                            vpnDownloadSpeed += state.DownloadSpeedBps;
                            vpnUploadSpeed += state.UploadSpeedBps;
                            vpnSessionReceived += sessionReceived;
                            vpnSessionSent += sessionSent;

                            // Track active VPN adapters (those with traffic)
                            if (state.DownloadSpeedBps > 0 || state.UploadSpeedBps > 0)
                            {
                                activeVpnAdapters.Add(state.Adapter.DisplayName);
                            }
                        }
                        else if (!state.IsVirtual)
                        {
                            // Physical adapter traffic (includes VPN overhead when VPN is active)
                            physicalDownloadSpeed += state.DownloadSpeedBps;
                            physicalUploadSpeed += state.UploadSpeedBps;
                            physicalSessionReceived += sessionReceived;
                            physicalSessionSent += sessionSent;
                        }
                    }
                }
                catch (NetworkInformationException ex)
                {
                    // Adapter might have been disconnected
                    _logger.LogWarning(ex, "Error reading stats for adapter {AdapterId}", nic.Id);
                }
            }

            // Determine what to display based on selection
            long displayDownloadSpeed;
            long displayUploadSpeed;
            long displaySessionReceived;
            long displaySessionSent;

            if (hasSelectedAdapter)
            {
                // Specific adapter selected - show its traffic
                displayDownloadSpeed = selectedDownloadSpeed;
                displayUploadSpeed = selectedUploadSpeed;
                displaySessionReceived = selectedSessionReceived;
                displaySessionSent = selectedSessionSent;
            }
            else
            {
                // "All Adapters" mode:
                // For speed: show VPN traffic if active (avoids double-counting), otherwise physical
                // For session totals: always show physical adapter totals (consistent, doesn't jump)
                if (vpnDownloadSpeed > 0 || vpnUploadSpeed > 0)
                {
                    // VPN is active - display actual payload traffic speed (not counting overhead twice)
                    displayDownloadSpeed = vpnDownloadSpeed;
                    displayUploadSpeed = vpnUploadSpeed;
                }
                else
                {
                    // No VPN - just show physical adapter traffic
                    displayDownloadSpeed = physicalDownloadSpeed;
                    displayUploadSpeed = physicalUploadSpeed;
                }

                // Session totals: always use physical adapters for consistency
                // This prevents the "jumping" between VPN and physical totals
                displaySessionReceived = physicalSessionReceived;
                displaySessionSent = physicalSessionSent;
            }

            // Determine if we have VPN traffic to analyze
            bool hasVpnTraffic = (vpnDownloadSpeed > 0 || vpnUploadSpeed > 0) &&
                                 (physicalDownloadSpeed > 0 || physicalUploadSpeed > 0);
            bool isVpnConnected = connectedVpnAdapters.Count > 0;

            _currentStats = new NetworkStats
            {
                Timestamp = DateTime.Now,
                DownloadSpeedBps = displayDownloadSpeed,
                UploadSpeedBps = displayUploadSpeed,
                SessionBytesReceived = displaySessionReceived,
                SessionBytesSent = displaySessionSent,
                AdapterId = _selectedAdapterId,
                ResolvedPrimaryAdapterId = resolvedAutoAdapterId,
                ResolvedPrimaryAdapterName = resolvedAutoAdapterName,

                // VPN analysis data
                IsVpnConnected = isVpnConnected,
                HasVpnTraffic = hasVpnTraffic,
                VpnDownloadSpeedBps = vpnDownloadSpeed,
                VpnUploadSpeedBps = vpnUploadSpeed,
                PhysicalDownloadSpeedBps = physicalDownloadSpeed,
                PhysicalUploadSpeedBps = physicalUploadSpeed,
                VpnSessionBytesReceived = vpnSessionReceived,
                VpnSessionBytesSent = vpnSessionSent,
                ConnectedVpnAdapters = connectedVpnAdapters,
                ActiveVpnAdapters = activeVpnAdapters
            };

            // Track primary adapter changes for notification
            if (isAutoMode && resolvedAutoAdapterId != _resolvedPrimaryAdapterId)
            {
                var previousId = _resolvedPrimaryAdapterId;
                _resolvedPrimaryAdapterId = resolvedAutoAdapterId;
                _resolvedPrimaryAdapterName = resolvedAutoAdapterName;

                if (!string.IsNullOrEmpty(previousId))
                {
                    _logger.LogInformation("Auto adapter switched from {OldAdapter} to {NewAdapter}",
                        previousId, resolvedAutoAdapterName);
                }
            }
        }

        // Fire event outside the lock to prevent deadlocks if handlers call back into this service
        StatsUpdated?.Invoke(this, _currentStats);
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

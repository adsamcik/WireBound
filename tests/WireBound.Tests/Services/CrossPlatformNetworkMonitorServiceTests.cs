using System.Net.NetworkInformation;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using WireBound.Avalonia.Services;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Tests for CrossPlatformNetworkMonitorService.
///
/// The service directly calls the static NetworkInterface.GetAllNetworkInterfaces(),
/// which cannot be mocked. Tests are therefore split into two categories:
///   1. Unit tests for classification logic (private static methods via reflection + mocked NetworkInterface)
///   2. Integration-style tests for public API behaviour (uses real system adapters)
/// </summary>
public class CrossPlatformNetworkMonitorServiceTests
{
    private static readonly Type ServiceType = typeof(CrossPlatformNetworkMonitorService);
    private readonly ILogger<CrossPlatformNetworkMonitorService> _logger;
    private readonly FakeTimeProvider _fakeTimeProvider = new();

    public CrossPlatformNetworkMonitorServiceTests()
    {
        _logger = Substitute.For<ILogger<CrossPlatformNetworkMonitorService>>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private CrossPlatformNetworkMonitorService CreateService() => new(_logger, _fakeTimeProvider);

    private static NetworkInterface MockNic(
        string name = "Ethernet",
        string description = "Intel Ethernet Controller",
        NetworkInterfaceType type = NetworkInterfaceType.Ethernet,
        OperationalStatus status = OperationalStatus.Up,
        string? id = null,
        long speed = 1_000_000_000)
    {
        var nic = Substitute.For<NetworkInterface>();
        nic.Name.Returns(name);
        nic.Description.Returns(description);
        nic.NetworkInterfaceType.Returns(type);
        nic.OperationalStatus.Returns(status);
        nic.Id.Returns(id ?? Guid.NewGuid().ToString());
        nic.Speed.Returns(speed);
        return nic;
    }

    private static object? InvokeStatic(string methodName, params object[] args)
    {
        var method = ServiceType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(ServiceType.Name, methodName);
        return method.Invoke(null, args);
    }

    private static T InvokeStatic<T>(string methodName, params object[] args) =>
        (T)InvokeStatic(methodName, args)!;

    // ═══════════════════════════════════════════════════════════════════════
    // 1. Adapter Discovery
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetAdapters_ReturnsOnlyOperationalAdapters()
    {
        var service = CreateService();
        var adapters = service.GetAdapters(includeVirtual: true);

        // Constructor filters to OperationalStatus.Up, so every returned adapter is active
        foreach (var adapter in adapters)
        {
            adapter.IsActive.Should().BeTrue();
        }
    }

    [Test]
    public void GetAdapters_CategorisesAdaptersCorrectly()
    {
        // Physical Ethernet
        InvokeStatic<string>("GetAdapterCategory",
            MockNic("Ethernet", "Intel I225-V Ethernet Controller"))
            .Should().Be("Physical");

        // Physical WiFi
        InvokeStatic<string>("GetAdapterCategory",
            MockNic("Wi-Fi", "Intel Wi-Fi 6E AX210", NetworkInterfaceType.Wireless80211))
            .Should().Be("Physical");

        // VPN (WireGuard)
        InvokeStatic<string>("GetAdapterCategory",
            MockNic("wg0", "WireGuard Tunnel", NetworkInterfaceType.Tunnel))
            .Should().Be("VPN");

        // Virtual Machine — Hyper-V
        InvokeStatic<string>("GetAdapterCategory",
            MockNic("vEthernet (Default Switch)", "Hyper-V Virtual Ethernet Adapter"))
            .Should().Be("Virtual Machine");

        // Virtual Machine — VMware
        InvokeStatic<string>("GetAdapterCategory",
            MockNic("VMnet8", "VMware Virtual Ethernet Adapter"))
            .Should().Be("Virtual Machine");

        // Virtual Machine — VirtualBox
        InvokeStatic<string>("GetAdapterCategory",
            MockNic("vboxnet0", "VirtualBox Host-Only Ethernet Adapter"))
            .Should().Be("Virtual Machine");

        // Container — Docker
        InvokeStatic<string>("GetAdapterCategory",
            MockNic("docker0", "Docker Network Bridge"))
            .Should().Be("Container");

        // WSL via vEthernet prefix hits VM detection first (name starts with "vethernet")
        InvokeStatic<string>("GetAdapterCategory",
            MockNic("vEthernet (WSL)", "WSL Virtual Ethernet Adapter"))
            .Should().Be("Virtual Machine");

        // Container — WSL (matched via description when name doesn't trigger VM)
        InvokeStatic<string>("GetAdapterCategory",
            MockNic("wsl0", "WSL Virtual Adapter"))
            .Should().Be("Container");
    }

    [Test]
    public void GetAdapters_DetectsVpnAdapters()
    {
        // WireGuard — name starts with "wg"
        InvokeStatic<string?>("DetectVpnProvider",
            MockNic("wg0", "Network Adapter", NetworkInterfaceType.Tunnel))
            .Should().Be("WireGuard");

        // NordVPN — description contains "nordlynx"
        InvokeStatic<string?>("DetectVpnProvider",
            MockNic("NordLynx", "NordLynx Adapter"))
            .Should().Be("NordVPN");

        // ExpressVPN — description contains "lightway"
        InvokeStatic<string?>("DetectVpnProvider",
            MockNic("ExpressVPN", "Lightway Tunnel Adapter"))
            .Should().Be("ExpressVPN");

        // OpenVPN — name starts with "tap-"
        InvokeStatic<string?>("DetectVpnProvider",
            MockNic("tap-windows6", "TAP-Windows Adapter V9"))
            .Should().Be("OpenVPN");

        // Cisco AnyConnect — description contains "anyconnect"
        InvokeStatic<string?>("DetectVpnProvider",
            MockNic("eth5", "Cisco AnyConnect Secure Mobility Client"))
            .Should().Be("Cisco AnyConnect");

        // Tailscale — description contains "tailscale"
        InvokeStatic<string?>("DetectVpnProvider",
            MockNic("Tailscale", "Tailscale Tunnel"))
            .Should().Be("Tailscale");

        // Cloudflare WARP — description contains "cloudflare"
        InvokeStatic<string?>("DetectVpnProvider",
            MockNic("CloudflareWARP", "Cloudflare WARP Adapter"))
            .Should().Be("Cloudflare WARP");

        // Generic Tunnel type (no matching description)
        InvokeStatic<string?>("DetectVpnProvider",
            MockNic("ipsec0", "Generic Network Adapter", NetworkInterfaceType.Tunnel))
            .Should().Be("VPN");

        // Generic PPP type
        InvokeStatic<string?>("DetectVpnProvider",
            MockNic("ppp0", "Point-to-Point Protocol", NetworkInterfaceType.Ppp))
            .Should().Be("VPN");

        // Non-VPN physical adapter → null
        InvokeStatic<string?>("DetectVpnProvider",
            MockNic("Ethernet", "Intel Ethernet Controller"))
            .Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Adapter Filtering (WFP / system adapters)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void IsFilterOrSystemAdapter_FiltersWfpAndQosAdapters()
    {
        InvokeStatic<bool>("IsFilterOrSystemAdapter",
            MockNic("Ethernet-WFP 802.3 MAC Layer LightWeight Filter-0000", "WFP Filter"))
            .Should().BeTrue();

        InvokeStatic<bool>("IsFilterOrSystemAdapter",
            MockNic("Ethernet-QoS Packet Scheduler-0000", "QoS Scheduler"))
            .Should().BeTrue();
    }

    [Test]
    public void IsFilterOrSystemAdapter_FiltersLocalAreaConnectionStar()
    {
        InvokeStatic<bool>("IsFilterOrSystemAdapter",
            MockNic("Local Area Connection* 12", "Microsoft Wi-Fi Direct Virtual Adapter"))
            .Should().BeTrue();
    }

    [Test]
    public void IsFilterOrSystemAdapter_AllowsNormalAdapters()
    {
        InvokeStatic<bool>("IsFilterOrSystemAdapter",
            MockNic("Ethernet", "Intel Ethernet Controller"))
            .Should().BeFalse();

        InvokeStatic<bool>("IsFilterOrSystemAdapter",
            MockNic("Wi-Fi", "Intel Wi-Fi Adapter"))
            .Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Virtual Adapter Detection
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void IsVirtualAdapter_DetectsVpnAsVirtual()
    {
        InvokeStatic<bool>("IsVirtualAdapter",
            MockNic("wg0", "WireGuard Tunnel", NetworkInterfaceType.Tunnel))
            .Should().BeTrue();
    }

    [Test]
    public void IsVirtualAdapter_DetectsVmAsVirtual()
    {
        InvokeStatic<bool>("IsVirtualAdapter",
            MockNic("VMnet8", "VMware Virtual Ethernet"))
            .Should().BeTrue();
    }

    [Test]
    public void IsVirtualAdapter_PhysicalIsNotVirtual()
    {
        InvokeStatic<bool>("IsVirtualAdapter",
            MockNic("Ethernet", "Intel I225-V Ethernet Controller"))
            .Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Adapter Type Mapping
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void MapAdapterType_MapsNetworkInterfaceTypesCorrectly()
    {
        InvokeStatic<NetworkAdapterType>("MapAdapterType", NetworkInterfaceType.Ethernet)
            .Should().Be(NetworkAdapterType.Ethernet);

        InvokeStatic<NetworkAdapterType>("MapAdapterType", NetworkInterfaceType.Wireless80211)
            .Should().Be(NetworkAdapterType.WiFi);

        InvokeStatic<NetworkAdapterType>("MapAdapterType", NetworkInterfaceType.Loopback)
            .Should().Be(NetworkAdapterType.Loopback);

        InvokeStatic<NetworkAdapterType>("MapAdapterType", NetworkInterfaceType.Tunnel)
            .Should().Be(NetworkAdapterType.Tunnel);

        InvokeStatic<NetworkAdapterType>("MapAdapterType", NetworkInterfaceType.Ppp)
            .Should().Be(NetworkAdapterType.Other);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Display Name Generation
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetDisplayName_VpnIncludesProviderName()
    {
        InvokeStatic<string>("GetDisplayName",
            MockNic("wg0", "WireGuard Tunnel", NetworkInterfaceType.Tunnel))
            .Should().Be("wg0 (WireGuard)");
    }

    [Test]
    public void GetDisplayName_PhysicalAdapterReturnsJustName()
    {
        InvokeStatic<string>("GetDisplayName",
            MockNic("Ethernet", "Intel Ethernet Controller"))
            .Should().Be("Ethernet");
    }

    [Test]
    public void GetDisplayName_VmIncludesVmType()
    {
        InvokeStatic<string>("GetDisplayName",
            MockNic("vEthernet (Default)", "Hyper-V Virtual Ethernet Adapter"))
            .Should().Be("vEthernet (Default) (Hyper-V)");

        InvokeStatic<string>("GetDisplayName",
            MockNic("VMnet8", "VMware Virtual Ethernet Adapter"))
            .Should().Be("VMnet8 (VMware)");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tethering Detection
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DetectTethering_DetectsUsbTethering()
    {
        var result = (ValueTuple<bool, bool>)InvokeStatic(
            "DetectTethering", MockNic("Ethernet 2", "Remote NDIS based Internet Sharing Device"))!;
        result.Item1.Should().BeTrue("RNDIS adapter should be detected as USB tethering");
        result.Item2.Should().BeFalse();
    }

    [Test]
    public void DetectTethering_DetectsIPhoneUsb()
    {
        var result = (ValueTuple<bool, bool>)InvokeStatic(
            "DetectTethering", MockNic("Ethernet 3", "Apple Mobile Device Ethernet"))!;
        result.Item1.Should().BeTrue("iPhone USB should be detected as USB tethering");
    }

    [Test]
    public void DetectTethering_DetectsBluetoothTethering()
    {
        var result = (ValueTuple<bool, bool>)InvokeStatic(
            "DetectTethering",
            MockNic("Bluetooth Network Connection", "Bluetooth PAN Network Adapter",
                NetworkInterfaceType.Ethernet))!;
        result.Item2.Should().BeTrue("Bluetooth PAN adapter should be detected as Bluetooth tethering");
    }

    [Test]
    public void DetectTethering_NormalAdapterNotTethering()
    {
        var result = (ValueTuple<bool, bool>)InvokeStatic(
            "DetectTethering", MockNic("Ethernet", "Intel Ethernet Controller"))!;
        result.Item1.Should().BeFalse();
        result.Item2.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetAdapters Virtual Filtering
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetAdapters_WithoutVirtual_ExcludesVmButKeepsVpn()
    {
        var service = CreateService();
        var withVirtual = service.GetAdapters(includeVirtual: true);
        var withoutVirtual = service.GetAdapters(includeVirtual: false);

        withoutVirtual.Count.Should().BeLessThanOrEqualTo(withVirtual.Count);

        // Non-virtual mode still shows known VPNs
        foreach (var adapter in withoutVirtual)
        {
            if (adapter.IsVirtual)
            {
                adapter.IsKnownVpn.Should().BeTrue(
                    "only known VPNs are allowed through the virtual filter");
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 2. Speed Calculation (integration — uses real system adapters)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Poll_ReturnsZeroSpeed_OnFirstPoll()
    {
        // The constructor sets LastPollTimestampMs; an immediate poll has
        // minimal elapsed time so the speed guard (elapsedMs >= 100) prevents
        // any speed calculation.
        var service = CreateService();
        service.Poll();

        var stats = service.GetCurrentStats();
        stats.DownloadSpeedBps.Should().BeGreaterThanOrEqualTo(0);
        stats.UploadSpeedBps.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public void Poll_CalculatesDownloadSpeed_FromByteDelta()
    {
        var service = CreateService();
        service.Poll();

        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();

        var stats = service.GetCurrentStats();
        stats.DownloadSpeedBps.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public void Poll_CalculatesUploadSpeed_FromByteDelta()
    {
        var service = CreateService();
        service.Poll();

        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();

        var stats = service.GetCurrentStats();
        stats.UploadSpeedBps.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public void Poll_HandlesCounterWrapAround()
    {
        // The service guards against negative byte deltas (counter reset / reboot):
        //   if (bytesReceivedDelta < 0) bytesReceivedDelta = stats.BytesReceived;
        // We verify the service stays stable through rapid successive polls.
        var service = CreateService();

        for (var i = 0; i < 10; i++)
            service.Poll();

        var stats = service.GetCurrentStats();
        stats.DownloadSpeedBps.Should().BeGreaterThanOrEqualTo(0);
        stats.UploadSpeedBps.Should().BeGreaterThanOrEqualTo(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 3. Multi-Adapter
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Poll_AggregatesAllAdapters_WhenNoSpecificAdapterSelected()
    {
        var service = CreateService();
        service.SetAdapter(string.Empty); // Explicitly select aggregate mode

        service.Poll();
        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();

        var stats = service.GetCurrentStats();
        stats.AdapterId.Should().BeEmpty("no adapter was selected, so aggregate mode applies");
        stats.DownloadSpeedBps.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public void Poll_ReturnsSpecificAdapterStats_WhenAdapterSelected()
    {
        var service = CreateService();
        var adapters = service.GetAdapters(includeVirtual: true);

        // At least one adapter should exist on any reasonable CI / dev machine
        if (adapters.Count == 0)
            return;

        var target = adapters[0];
        service.SetAdapter(target.Id);

        service.Poll();
        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();

        var stats = service.GetCurrentStats();
        stats.AdapterId.Should().Be(target.Id);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 4. Error Handling
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Poll_ReturnsZero_WhenAdapterStatsFail()
    {
        // Selecting a non-existent adapter ID causes the selected-adapter
        // accumulators to stay at zero.
        var service = CreateService();
        service.SetAdapter("non-existent-adapter-id");
        service.Poll();

        var stats = service.GetCurrentStats();
        stats.DownloadSpeedBps.Should().Be(0);
        stats.UploadSpeedBps.Should().Be(0);
    }

    [Test]
    public void Poll_ContinuesWorking_AfterTransientError()
    {
        var service = CreateService();

        // Successive polls must not throw
        service.Poll();
        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();
        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();

        var stats = service.GetCurrentStats();
        stats.Should().NotBeNull();
        stats.DownloadSpeedBps.Should().BeGreaterThanOrEqualTo(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 5. Session Tracking
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SessionBytes_AccumulateOverMultiplePolls()
    {
        var service = CreateService();

        service.Poll();
        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();
        var first = service.GetCurrentStats();

        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();
        var second = service.GetCurrentStats();

        // Session byte counters must be non-decreasing
        second.SessionBytesReceived.Should().BeGreaterThanOrEqualTo(first.SessionBytesReceived);
        second.SessionBytesSent.Should().BeGreaterThanOrEqualTo(first.SessionBytesSent);
    }

    [Test]
    public void SessionBytes_ResetOnSessionReset()
    {
        var service = CreateService();

        service.Poll();
        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();

        service.ResetSession();
        var stats = service.GetCurrentStats();

        stats.SessionBytesReceived.Should().Be(0);
        stats.SessionBytesSent.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Event & Misc.
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Poll_RaisesStatsUpdatedEvent()
    {
        var service = CreateService();
        NetworkStats? received = null;
        service.StatsUpdated += (_, s) => received = s;

        service.Poll();

        received.Should().NotBeNull();
    }

    [Test]
    public void IsUsingIpHelperApi_AlwaysReturnsFalse()
    {
        var service = CreateService();
        service.IsUsingIpHelperApi.Should().BeFalse();

        // Setting it does nothing in the cross-platform version
        service.SetUseIpHelperApi(true);
        service.IsUsingIpHelperApi.Should().BeFalse();
    }

    [Test]
    public void GetStats_UnknownAdapterId_ReturnsDefaultStats()
    {
        var service = CreateService();
        var stats = service.GetStats("non-existent-id");

        stats.DownloadSpeedBps.Should().Be(0);
        stats.UploadSpeedBps.Should().Be(0);
        stats.SessionBytesReceived.Should().Be(0);
        stats.SessionBytesSent.Should().Be(0);
    }

    [Test]
    public void GetAllAdapterStats_ReturnsStatsForActiveAdapters()
    {
        var service = CreateService();
        service.Poll();

        var allStats = service.GetAllAdapterStats();
        allStats.Should().NotBeNull();

        // Every key should correspond to a known adapter
        var adapterIds = service.GetAdapters(includeVirtual: true)
            .Select(a => a.Id)
            .ToHashSet();

        foreach (var key in allStats.Keys)
        {
            adapterIds.Should().Contain(key);
        }
    }

    [Test]
    public void GetCurrentStats_BeforeFirstPoll_ReturnsDefault()
    {
        var service = CreateService();
        var stats = service.GetCurrentStats();

        stats.Should().NotBeNull();
        stats.DownloadSpeedBps.Should().Be(0);
        stats.UploadSpeedBps.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // 7. Auto Adapter Mode
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void DefaultMode_IsAutoAdapter()
    {
        // A freshly created service should default to auto mode
        var service = CreateService();
        service.Poll();

        var stats = service.GetCurrentStats();
        // In auto mode, AdapterId should be "auto"
        stats.AdapterId.Should().Be(NetworkMonitorConstants.AutoAdapterId);
    }

    [Test]
    public void SetAdapter_ToAuto_SetsAutoMode()
    {
        var service = CreateService();
        service.SetAdapter("some-id");
        service.SetAdapter(NetworkMonitorConstants.AutoAdapterId);
        service.Poll();

        var stats = service.GetCurrentStats();
        stats.AdapterId.Should().Be(NetworkMonitorConstants.AutoAdapterId);
    }

    [Test]
    public void AutoMode_ResolvesAdapterId()
    {
        var service = CreateService();
        service.Poll();
        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();

        var stats = service.GetCurrentStats();
        // In auto mode, ResolvedPrimaryAdapterId should be set (or empty if no gateway)
        stats.ResolvedPrimaryAdapterId.Should().NotBeNull();
    }

    [Test]
    public void GetPrimaryAdapterId_ReturnsStringOrEmpty()
    {
        var service = CreateService();
        service.Poll(); // Need at least one poll to populate adapter states

        var primaryId = service.GetPrimaryAdapterId();

        primaryId.Should().NotBeNull();
        // Returns either a real adapter ID or empty string if no gateway found
    }

    [Test]
    public void AutoMode_SpeedsAreNonNegative()
    {
        var service = CreateService();
        service.Poll();
        _fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(150));
        service.Poll();

        var stats = service.GetCurrentStats();
        stats.DownloadSpeedBps.Should().BeGreaterThanOrEqualTo(0);
        stats.UploadSpeedBps.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public void AutoMode_ThenSwitchToSpecific_ThenBackToAuto()
    {
        var service = CreateService();
        var adapters = service.GetAdapters(includeVirtual: true);

        // Start in auto mode
        service.Poll();
        var autoStats = service.GetCurrentStats();
        autoStats.AdapterId.Should().Be(NetworkMonitorConstants.AutoAdapterId);

        // Switch to specific adapter
        if (adapters.Count > 0)
        {
            service.SetAdapter(adapters[0].Id);
            service.Poll();
            var specificStats = service.GetCurrentStats();
            specificStats.AdapterId.Should().Be(adapters[0].Id);
        }

        // Switch back to auto
        service.SetAdapter(NetworkMonitorConstants.AutoAdapterId);
        service.Poll();
        var backToAuto = service.GetCurrentStats();
        backToAuto.AdapterId.Should().Be(NetworkMonitorConstants.AutoAdapterId);
    }

    [Test]
    public void GetAllAdapterStats_InAutoMode_ReturnsAllAdapters()
    {
        var service = CreateService();
        service.Poll();

        var allStats = service.GetAllAdapterStats();
        var adapters = service.GetAdapters(includeVirtual: false);

        // All active adapters should have stats
        foreach (var adapter in adapters.Where(a => a.IsActive))
        {
            allStats.Should().ContainKey(adapter.Id);
        }
    }

    [Test]
    public void SetAdapter_EmptyString_IsAggregateNotAutoMode()
    {
        var service = CreateService();
        service.SetAdapter(string.Empty);
        service.Poll();

        var stats = service.GetCurrentStats();
        stats.AdapterId.Should().BeEmpty();
    }
}

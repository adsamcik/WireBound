using System.Globalization;
using System.Net;
using WireBound.Elevation.Linux;

namespace WireBound.Tests.IPC;

/// <summary>
/// Tests for NetlinkConnectionTracker — ParseHexEndpoint (the actual method,
/// not a copy), connection state management, and stale entry cleanup.
/// These tests call the internal methods directly via InternalsVisibleTo.
/// </summary>
public class NetlinkConnectionTrackerTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // ParseHexEndpoint — IPv4 (mirrors ProcNetTcpParsingTests but uses real method)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseHexEndpoint_Ipv4_Loopback()
    {
        var result = NetlinkConnectionTracker.ParseHexEndpoint("0100007F:0050", isIpv6: false);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Loopback);
        result.Port.Should().Be(80);
    }

    [Test]
    public void ParseHexEndpoint_Ipv4_Any()
    {
        var result = NetlinkConnectionTracker.ParseHexEndpoint("00000000:01BB", isIpv6: false);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Any);
        result.Port.Should().Be(443);
    }

    [Test]
    public void ParseHexEndpoint_Ipv4_Typical()
    {
        // 192.168.1.1 → C0.A8.01.01 → LE: 0101A8C0
        var result = NetlinkConnectionTracker.ParseHexEndpoint("0101A8C0:1F90", isIpv6: false);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Parse("192.168.1.1"));
        result.Port.Should().Be(8080);
    }

    [Test]
    public void ParseHexEndpoint_Ipv4_WrongLength_ReturnsNull()
    {
        NetlinkConnectionTracker.ParseHexEndpoint("0100:0050", isIpv6: false).Should().BeNull();
    }

    [Test]
    public void ParseHexEndpoint_Ipv4_NoColon_ReturnsNull()
    {
        NetlinkConnectionTracker.ParseHexEndpoint("0100007F0050", isIpv6: false).Should().BeNull();
    }

    [Test]
    public void ParseHexEndpoint_Ipv4_InvalidHex_ReturnsNull()
    {
        NetlinkConnectionTracker.ParseHexEndpoint("ZZZZZZZZ:0050", isIpv6: false).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ParseHexEndpoint — IPv6
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseHexEndpoint_Ipv6_Loopback()
    {
        var result = NetlinkConnectionTracker.ParseHexEndpoint(
            "00000000000000000000000001000000:0050", isIpv6: true);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.IPv6Loopback);
        result.Port.Should().Be(80);
    }

    [Test]
    public void ParseHexEndpoint_Ipv6_Any()
    {
        var result = NetlinkConnectionTracker.ParseHexEndpoint(
            "00000000000000000000000000000000:01BB", isIpv6: true);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.IPv6Any);
        result.Port.Should().Be(443);
    }

    [Test]
    public void ParseHexEndpoint_Ipv6_MappedIpv4()
    {
        // ::ffff:127.0.0.1
        var result = NetlinkConnectionTracker.ParseHexEndpoint(
            "0000000000000000FFFF00000100007F:0050", isIpv6: true);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Parse("::ffff:127.0.0.1"));
    }

    [Test]
    public void ParseHexEndpoint_Ipv6_RealAddress()
    {
        // 2001:0db8::1 → stored as B80D0120 00000000 00000000 01000000
        var result = NetlinkConnectionTracker.ParseHexEndpoint(
            "B80D0120000000000000000001000000:0050", isIpv6: true);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Parse("2001:0db8::1"));
    }

    [Test]
    public void ParseHexEndpoint_Ipv6_LinkLocal()
    {
        // fe80::1
        var result = NetlinkConnectionTracker.ParseHexEndpoint(
            "000080FE000000000000000001000000:0050", isIpv6: true);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Parse("fe80::1"));
    }

    [Test]
    public void ParseHexEndpoint_Ipv6_WrongLength_ReturnsNull()
    {
        // 31 chars instead of 32
        NetlinkConnectionTracker.ParseHexEndpoint(
            "B80D012000000000000000000100000:0050", isIpv6: true).Should().BeNull();
    }

    [Test]
    public void ParseHexEndpoint_Ipv6_InvalidHex_ReturnsNull()
    {
        NetlinkConnectionTracker.ParseHexEndpoint(
            "ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ:0050", isIpv6: true).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Port parsing
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseHexEndpoint_HighPort()
    {
        // Port 65535 = 0xFFFF
        var result = NetlinkConnectionTracker.ParseHexEndpoint("0100007F:FFFF", isIpv6: false);
        result.Should().NotBeNull();
        result!.Port.Should().Be(65535);
    }

    [Test]
    public void ParseHexEndpoint_ZeroPort()
    {
        var result = NetlinkConnectionTracker.ParseHexEndpoint("0100007F:0000", isIpv6: false);
        result.Should().NotBeNull();
        result!.Port.Should().Be(0);
    }

    [Test]
    public void ParseHexEndpoint_InvalidPortHex_ReturnsNull()
    {
        NetlinkConnectionTracker.ParseHexEndpoint("0100007F:ZZZZ", isIpv6: false).Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Tracker lifecycle
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Tracker_GetConnectionStats_BeforeStart_ReturnsEmpty()
    {
        using var tracker = new NetlinkConnectionTracker();
        var stats = tracker.GetConnectionStats();
        stats.Success.Should().BeTrue();
        stats.Processes.Should().BeEmpty();
    }

    [Test]
    public void Tracker_GetProcessStats_BeforeStart_ReturnsEmpty()
    {
        using var tracker = new NetlinkConnectionTracker();
        var stats = tracker.GetProcessStats([]);
        stats.Success.Should().BeTrue();
        stats.Processes.Should().BeEmpty();
    }

    [Test]
    public void Tracker_GetProcessStats_WithFilterPids_ReturnsEmpty()
    {
        using var tracker = new NetlinkConnectionTracker();
        var stats = tracker.GetProcessStats([99999, 88888]);
        stats.Success.Should().BeTrue();
        stats.Processes.Should().BeEmpty();
    }

    [Test]
    public void Tracker_Dispose_BeforeStart_DoesNotThrow()
    {
        var tracker = new NetlinkConnectionTracker();
        var act = () => tracker.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public void Tracker_Dispose_CalledTwice_DoesNotThrow()
    {
        var tracker = new NetlinkConnectionTracker();
        tracker.Dispose();
        var act = () => tracker.Dispose();
        act.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // All IPv4 address variety
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseHexEndpoint_Ipv4_Broadcast()
    {
        // 255.255.255.255 → FF.FF.FF.FF → LE: FFFFFFFF
        var result = NetlinkConnectionTracker.ParseHexEndpoint("FFFFFFFF:0050", isIpv6: false);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Broadcast);
    }

    [Test]
    public void ParseHexEndpoint_Ipv4_PrivateRange()
    {
        // 10.0.0.1 → 0A.00.00.01 → LE: 0100000A
        var result = NetlinkConnectionTracker.ParseHexEndpoint("0100000A:0050", isIpv6: false);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Parse("10.0.0.1"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IPv6 full address (all groups non-zero)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseHexEndpoint_Ipv6_AllOnes()
    {
        // ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff
        // Each group: FFFF:FFFF → bytes FF FF FF FF → LE hex: FFFFFFFF
        var result = NetlinkConnectionTracker.ParseHexEndpoint(
            "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:0050", isIpv6: true);
        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Parse("ffff:ffff:ffff:ffff:ffff:ffff:ffff:ffff"));
    }
}

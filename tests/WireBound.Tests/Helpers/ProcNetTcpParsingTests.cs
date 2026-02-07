using System.Globalization;
using System.Net;
using AwesomeAssertions;

namespace WireBound.Tests.Helpers;

/// <summary>
/// Tests the /proc/net/tcp6 IPv6 address parsing algorithm.
/// Linux stores IPv6 addresses as 4×32-bit little-endian integers, NOT as sequential bytes.
/// These tests validate the correct byte-swapping logic used in NetlinkConnectionTracker.
/// </summary>
public class ProcNetTcpParsingTests
{
    /// <summary>
    /// Parses hex-encoded IP:port from /proc/net/tcp format — mirrors
    /// NetlinkConnectionTracker.ParseHexEndpoint for testability on all platforms.
    /// </summary>
    private static IPEndPoint? ParseHexEndpoint(string hexEndpoint, bool isIpv6)
    {
        var colonIdx = hexEndpoint.IndexOf(':');
        if (colonIdx < 0) return null;

        var addrHex = hexEndpoint[..colonIdx];
        var portHex = hexEndpoint[(colonIdx + 1)..];

        if (!int.TryParse(portHex, NumberStyles.HexNumber, null, out var port))
            return null;

        try
        {
            if (isIpv6)
            {
                if (addrHex.Length != 32) return null;

                var bytes = new byte[16];
                for (var g = 0; g < 4; g++)
                {
                    var groupHex = addrHex.AsSpan(g * 8, 8);
                    var hostOrder = uint.Parse(groupHex, NumberStyles.HexNumber);
                    bytes[g * 4 + 0] = (byte)(hostOrder & 0xFF);
                    bytes[g * 4 + 1] = (byte)((hostOrder >> 8) & 0xFF);
                    bytes[g * 4 + 2] = (byte)((hostOrder >> 16) & 0xFF);
                    bytes[g * 4 + 3] = (byte)((hostOrder >> 24) & 0xFF);
                }
                return new IPEndPoint(new IPAddress(bytes), port);
            }
            else
            {
                if (addrHex.Length != 8) return null;
                var ipInt = uint.Parse(addrHex, NumberStyles.HexNumber);
                var bytes = BitConverter.GetBytes(ipInt);
                return new IPEndPoint(new IPAddress(bytes), port);
            }
        }
        catch
        {
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IPv6 parsing — 4×32-bit LE groups
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseHexEndpoint_Ipv6Loopback_ReturnsCorrectAddress()
    {
        // ::1 is stored as "00000000000000000000000001000000" in /proc/net/tcp6
        // The last group "01000000" is 0x00000001 in little-endian
        var result = ParseHexEndpoint("00000000000000000000000001000000:0050", isIpv6: true);

        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.IPv6Loopback);
        result.Port.Should().Be(80);
    }

    [Test]
    public void ParseHexEndpoint_Ipv6AllZeros_ReturnsAny()
    {
        // :: (all zeros)
        var result = ParseHexEndpoint("00000000000000000000000000000000:01BB", isIpv6: true);

        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.IPv6Any);
        result.Port.Should().Be(443);
    }

    [Test]
    public void ParseHexEndpoint_Ipv6MappedIpv4_ReturnsCorrectAddress()
    {
        // ::ffff:127.0.0.1 is stored as "0000000000000000FFFF00000100007F" in /proc/net/tcp6
        // Group 0: 00000000 → 0x00000000
        // Group 1: 00000000 → 0x00000000
        // Group 2: FFFF0000 → 0x0000FFFF (LE) → bytes: 00 00 FF FF
        // Group 3: 0100007F → 0x7F000001 (LE) → bytes: 01 00 00 7F → 127.0.0.1
        var result = ParseHexEndpoint("0000000000000000FFFF00000100007F:0050", isIpv6: true);

        result.Should().NotBeNull();
        var expected = IPAddress.Parse("::ffff:127.0.0.1");
        result!.Address.Should().Be(expected);
        result.Port.Should().Be(80);
    }

    [Test]
    public void ParseHexEndpoint_Ipv6RealAddress_ReturnsCorrectAddress()
    {
        // 2001:0db8::1 = 2001:0db8:0000:0000:0000:0000:0000:0001
        // Stored in /proc/net/tcp6 as 4 LE 32-bit groups:
        // Group 0: 2001:0db8 → network bytes 20 01 0d b8 → as 32-bit LE: B80D0120
        // Group 1: 0000:0000 → 00000000
        // Group 2: 0000:0000 → 00000000
        // Group 3: 0000:0001 → network bytes 00 00 00 01 → as 32-bit LE: 01000000
        var result = ParseHexEndpoint("B80D012000000000000000000100000:0050", isIpv6: true);

        // 31 chars — invalid (must be 32)
        result.Should().BeNull();

        // Correct 32-char form
        result = ParseHexEndpoint("B80D0120000000000000000001000000:0050", isIpv6: true);
        result.Should().NotBeNull();
        var expected = IPAddress.Parse("2001:0db8::1");
        result!.Address.Should().Be(expected);
    }

    [Test]
    public void ParseHexEndpoint_Ipv6_Fe80LinkLocal_ReturnsCorrectAddress()
    {
        // fe80::1 = fe80:0000:0000:0000:0000:0000:0000:0001
        // Group 0: fe80:0000 → network bytes FE 80 00 00 → as 32-bit LE: 000080FE
        // Group 1: 00000000
        // Group 2: 00000000
        // Group 3: 0000:0001 → as 32-bit LE: 01000000
        var result = ParseHexEndpoint("000080FE000000000000000001000000:0050", isIpv6: true);

        result.Should().NotBeNull();
        var expected = IPAddress.Parse("fe80::1");
        result!.Address.Should().Be(expected);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // IPv4 parsing (unchanged — regression tests)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseHexEndpoint_Ipv4Loopback_ReturnsCorrectAddress()
    {
        // 127.0.0.1:80 → "0100007F:0050" (little-endian)
        var result = ParseHexEndpoint("0100007F:0050", isIpv6: false);

        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Loopback);
        result.Port.Should().Be(80);
    }

    [Test]
    public void ParseHexEndpoint_Ipv4AllZeros_ReturnsAny()
    {
        var result = ParseHexEndpoint("00000000:01BB", isIpv6: false);

        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Any);
        result.Port.Should().Be(443);
    }

    [Test]
    public void ParseHexEndpoint_Ipv4TypicalAddress_ReturnsCorrectAddress()
    {
        // 192.168.1.1 → C0.A8.01.01 → little-endian hex: 0101A8C0
        var result = ParseHexEndpoint("0101A8C0:1F90", isIpv6: false);

        result.Should().NotBeNull();
        result!.Address.Should().Be(IPAddress.Parse("192.168.1.1"));
        result.Port.Should().Be(8080);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseHexEndpoint_NoColon_ReturnsNull()
    {
        ParseHexEndpoint("0100007F0050", isIpv6: false).Should().BeNull();
    }

    [Test]
    public void ParseHexEndpoint_InvalidLength_ReturnsNull()
    {
        ParseHexEndpoint("0100:0050", isIpv6: false).Should().BeNull();
        ParseHexEndpoint("0100:0050", isIpv6: true).Should().BeNull();
    }

    [Test]
    public void ParseHexEndpoint_InvalidHex_ReturnsNull()
    {
        ParseHexEndpoint("ZZZZZZZZ:0050", isIpv6: false).Should().BeNull();
    }
}

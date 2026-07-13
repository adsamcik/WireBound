using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Tests.Platform;

/// <summary>
/// Tests for the shared listener-owner attribution used to recover the owning app
/// for loopback/transient connections the OS reports with PID 0.
/// </summary>
public class ConnectionAttributionTests
{
    private static readonly Dictionary<int, int> Listeners = new()
    {
        [5037] = 4242, // adb listening on 5037
        [8080] = 9999,
    };

    [Test]
    public void ResolveOwnerPid_NonZeroPid_ReturnsOriginal()
    {
        ConnectionAttribution.ResolveOwnerPid("TCP", 1234, 5037, 56000, Listeners)
            .Should().Be(1234);
    }

    [Test]
    public void ResolveOwnerPid_NonTcp_ReturnsOriginal()
    {
        ConnectionAttribution.ResolveOwnerPid("UDP", 0, 5037, 0, Listeners)
            .Should().Be(0);
    }

    [Test]
    public void ResolveOwnerPid_ZeroPid_UsesLocalPortListener()
    {
        // local port 5037 is a listener -> this is the server-side socket, owned by adb.
        ConnectionAttribution.ResolveOwnerPid("TCP", 0, 5037, 56000, Listeners)
            .Should().Be(4242);
    }

    [Test]
    public void ResolveOwnerPid_ZeroPid_FallsBackToRemotePortListener()
    {
        // local port is ephemeral (no listener), remote port 8080 is a listener.
        ConnectionAttribution.ResolveOwnerPid("TCP", 0, 56000, 8080, Listeners)
            .Should().Be(9999);
    }

    [Test]
    public void ResolveOwnerPid_ZeroPid_PrefersLocalOverRemote()
    {
        ConnectionAttribution.ResolveOwnerPid("TCP", 0, 5037, 8080, Listeners)
            .Should().Be(4242);
    }

    [Test]
    public void ResolveOwnerPid_ZeroPid_NoListenerMatch_ReturnsZero()
    {
        ConnectionAttribution.ResolveOwnerPid("TCP", 0, 56000, 56001, Listeners)
            .Should().Be(0);
    }

    [Test]
    public void BuildTcpListenerMap_OnlyIncludesTcpListenersWithRealPid()
    {
        var conns = new[]
        {
            ("TCP", ConnectionState.Listen, 5037, 4242),       // included
            ("TCP", ConnectionState.Listen, 9000, 0),          // excluded: pid 0
            ("TCP", ConnectionState.Established, 6000, 1111),  // excluded: not listening
            ("UDP", ConnectionState.Listen, 5353, 2222),       // excluded: not TCP
        };

        var map = ConnectionAttribution.BuildTcpListenerMap(
            conns, c => c.Item1, c => c.Item2, c => c.Item3, c => c.Item4);

        map.Should().ContainKey(5037);
        map[5037].Should().Be(4242);
        map.Should().NotContainKey(9000);
        map.Should().NotContainKey(6000);
        map.Should().NotContainKey(5353);
    }
}

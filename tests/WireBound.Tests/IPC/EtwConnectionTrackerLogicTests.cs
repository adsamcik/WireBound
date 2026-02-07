using WireBound.Elevation.Windows;

namespace WireBound.Tests.IPC;

/// <summary>
/// Tests for EtwConnectionTracker logic — connection key format,
/// stats aggregation, and boundary conditions.
/// These tests exercise the internal methods extracted for testability.
/// </summary>
public class EtwConnectionTrackerLogicTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // MakeConnectionKey
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void MakeConnectionKey_FormatsCorrectly()
    {
        var key = EtwConnectionTracker.MakeConnectionKey("192.168.1.1", 12345, "8.8.8.8", 443);
        key.Should().Be("192.168.1.1:12345-8.8.8.8:443");
    }

    [Test]
    public void MakeConnectionKey_IPv6_FormatsCorrectly()
    {
        var key = EtwConnectionTracker.MakeConnectionKey("::1", 80, "::1", 8080);
        key.Should().Be("::1:80-::1:8080");
    }

    [Test]
    public void MakeConnectionKey_ZeroPorts()
    {
        var key = EtwConnectionTracker.MakeConnectionKey("0.0.0.0", 0, "0.0.0.0", 0);
        key.Should().Be("0.0.0.0:0-0.0.0.0:0");
    }

    [Test]
    public void MakeConnectionKey_HighPorts()
    {
        var key = EtwConnectionTracker.MakeConnectionKey("127.0.0.1", 65535, "10.0.0.1", 65534);
        key.Should().Be("127.0.0.1:65535-10.0.0.1:65534");
    }

    [Test]
    public void MakeConnectionKey_SameEndpoints_DifferentDirection_DifferentKeys()
    {
        var key1 = EtwConnectionTracker.MakeConnectionKey("A", 1, "B", 2);
        var key2 = EtwConnectionTracker.MakeConnectionKey("B", 2, "A", 1);
        key1.Should().NotBe(key2, "direction matters");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetConnectionStats — empty state
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetConnectionStats_BeforeStart_ReturnsSuccess()
    {
        using var tracker = new EtwConnectionTracker();
        var stats = tracker.GetConnectionStats();

        stats.Success.Should().BeTrue();
        stats.Processes.Should().NotBeNull();
    }

    [Test]
    public void GetProcessStats_BeforeStart_ReturnsSuccess()
    {
        using var tracker = new EtwConnectionTracker();
        var stats = tracker.GetProcessStats([]);

        stats.Success.Should().BeTrue();
        stats.Processes.Should().NotBeNull();
    }

    [Test]
    public void GetProcessStats_WithFilter_ReturnsEmpty()
    {
        using var tracker = new EtwConnectionTracker();
        var stats = tracker.GetProcessStats([99999]);

        stats.Success.Should().BeTrue();
        stats.Processes.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dispose
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void Dispose_BeforeStart_DoesNotThrow()
    {
        var tracker = new EtwConnectionTracker();
        var act = () => tracker.Dispose();
        act.Should().NotThrow();
    }

    [Test]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var tracker = new EtwConnectionTracker();
        tracker.Dispose();
        var act = () => tracker.Dispose();
        act.Should().NotThrow();
    }
}

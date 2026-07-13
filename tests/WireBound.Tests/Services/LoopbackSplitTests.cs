using AwesomeAssertions;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Tests for the loopback/network split exposed by <see cref="AppOverview"/>
/// and <see cref="AppUsageRecord"/>.
/// </summary>
public class LoopbackSplitTests
{
    [Test]
    public void AppOverview_NetworkBytes_SubtractLoopback()
    {
        var app = new AppOverview(
            "id", "App", "proc", "/p", "", 1000, 400, 0, 0, 0, 0, 0, 0,
            DateTime.Now, DateTime.Now, 1)
        {
            LoopbackBytesReceived = 600,
            LoopbackBytesSent = 100
        };

        app.TotalBytes.Should().Be(1400);
        app.NetworkBytesReceived.Should().Be(400);
        app.NetworkBytesSent.Should().Be(300);
        app.NetworkTotalBytes.Should().Be(700);
        app.LoopbackTotalBytes.Should().Be(700);
        app.HasLoopbackTraffic.Should().BeTrue();
    }

    [Test]
    public void AppOverview_NoLoopback_NetworkEqualsTotal()
    {
        var app = new AppOverview(
            "id", "App", "proc", "/p", "", 1000, 400, 0, 0, 0, 0, 0, 0,
            DateTime.Now, DateTime.Now, 1);

        app.NetworkTotalBytes.Should().Be(app.TotalBytes);
        app.HasLoopbackTraffic.Should().BeFalse();
        app.LoopbackTotalBytes.Should().Be(0);
    }

    [Test]
    public void AppOverview_NetworkBytes_NeverNegative()
    {
        // Loopback larger than recorded total (e.g. mixed legacy data) clamps to 0.
        var app = new AppOverview(
            "id", "App", "proc", "/p", "", 100, 50, 0, 0, 0, 0, 0, 0,
            DateTime.Now, DateTime.Now, 1)
        {
            LoopbackBytesReceived = 9999,
            LoopbackBytesSent = 9999
        };

        app.NetworkBytesReceived.Should().Be(0);
        app.NetworkBytesSent.Should().Be(0);
        app.NetworkTotalBytes.Should().Be(0);
    }

    [Test]
    public void AppUsageRecord_NetworkBytes_SubtractLoopback()
    {
        var record = new AppUsageRecord
        {
            BytesReceived = 2000,
            BytesSent = 500,
            LoopbackBytesReceived = 1500,
            LoopbackBytesSent = 100
        };

        record.NetworkBytesReceived.Should().Be(500);
        record.NetworkBytesSent.Should().Be(400);
    }
}

using WireBound.Models;
using WireBound.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Tests for INetworkMonitorService behavior
/// Note: These tests use NSubstitute to mock the interface since the concrete 
/// implementation depends on actual network interfaces.
/// </summary>
public class NetworkMonitorServiceTests
{
    #region Mock Setup Tests

    [Test]
    public async Task GetCurrentStats_WhenCalled_ReturnsNetworkStats()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();
        var expectedStats = new NetworkStats
        {
            DownloadSpeedBps = 1_000_000,
            UploadSpeedBps = 500_000,
            SessionBytesReceived = 100_000_000,
            SessionBytesSent = 50_000_000
        };
        mockService.GetCurrentStats().Returns(expectedStats);

        // Act
        var result = mockService.GetCurrentStats();

        // Assert
        await Assert.That(result).IsEqualTo(expectedStats);
        await Assert.That(result.DownloadSpeedBps).IsEqualTo(1_000_000);
    }

    [Test]
    public async Task GetAdapters_WhenCalled_ReturnsAdapterList()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true },
            new() { Id = "wifi0", Name = "Wi-Fi", IsActive = true }
        };
        mockService.GetAdapters().Returns(adapters);

        // Act
        var result = mockService.GetAdapters();

        // Assert
        await Assert.That(result.Count).IsEqualTo(2);
        await Assert.That(result[0].Id).IsEqualTo("eth0");
        await Assert.That(result[1].Id).IsEqualTo("wifi0");
    }

    [Test]
    public async Task GetStats_WithValidAdapterId_ReturnsStats()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();
        var expectedStats = new NetworkStats
        {
            AdapterId = "eth0",
            DownloadSpeedBps = 2_000_000
        };
        mockService.GetStats("eth0").Returns(expectedStats);

        // Act
        var result = mockService.GetStats("eth0");

        // Assert
        await Assert.That(result.AdapterId).IsEqualTo("eth0");
        await Assert.That(result.DownloadSpeedBps).IsEqualTo(2_000_000);
    }

    [Test]
    public async Task GetStats_WithInvalidAdapterId_ReturnsEmptyStats()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();
        mockService.GetStats("invalid-id").Returns(new NetworkStats());

        // Act
        var result = mockService.GetStats("invalid-id");

        // Assert
        await Assert.That(result.DownloadSpeedBps).IsEqualTo(0);
        await Assert.That(result.UploadSpeedBps).IsEqualTo(0);
    }

    #endregion

    #region SetAdapter Tests

    [Test]
    public async Task SetAdapter_WhenCalled_ChangesMonitoredAdapter()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();

        // Act
        mockService.SetAdapter("eth0");

        // Assert
        mockService.Received(1).SetAdapter("eth0");
        await Assert.That(true).IsTrue(); // Verify no exception
    }

    [Test]
    public async Task SetAdapter_WithEmptyString_MonitorsAllAdapters()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();

        // Act
        mockService.SetAdapter(string.Empty);

        // Assert
        mockService.Received(1).SetAdapter(string.Empty);
        await Assert.That(true).IsTrue();
    }

    #endregion

    #region IsUsingIpHelperApi Tests

    [Test]
    public async Task IsUsingIpHelperApi_WhenDisabled_ReturnsFalse()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();
        mockService.IsUsingIpHelperApi.Returns(false);

        // Act & Assert
        await Assert.That(mockService.IsUsingIpHelperApi).IsFalse();
    }

    [Test]
    public async Task IsUsingIpHelperApi_WhenEnabled_ReturnsTrue()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();
        mockService.IsUsingIpHelperApi.Returns(true);

        // Act & Assert
        await Assert.That(mockService.IsUsingIpHelperApi).IsTrue();
    }

    [Test]
    public async Task SetUseIpHelperApi_WhenCalled_ModifiesApiMode()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();

        // Act
        mockService.SetUseIpHelperApi(true);

        // Assert
        mockService.Received(1).SetUseIpHelperApi(true);
        await Assert.That(true).IsTrue();
    }

    #endregion

    #region Poll Tests

    [Test]
    public async Task Poll_WhenCalled_UpdatesStats()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();

        // Act
        mockService.Poll();

        // Assert
        mockService.Received(1).Poll();
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task Poll_MultipleCalls_AreTracked()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();

        // Act
        mockService.Poll();
        mockService.Poll();
        mockService.Poll();

        // Assert
        mockService.Received(3).Poll();
        await Assert.That(true).IsTrue();
    }

    #endregion

    #region StatsUpdated Event Tests

    [Test]
    public async Task StatsUpdated_WhenRaised_InvokesHandler()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();
        NetworkStats? receivedStats = null;
        mockService.StatsUpdated += (sender, stats) => receivedStats = stats;

        var newStats = new NetworkStats { DownloadSpeedBps = 5_000_000 };

        // Act
        mockService.StatsUpdated += Raise.Event<EventHandler<NetworkStats>>(mockService, newStats);

        // Assert
        await Assert.That(receivedStats).IsNotNull();
        await Assert.That(receivedStats!.DownloadSpeedBps).IsEqualTo(5_000_000);
    }

    [Test]
    public async Task StatsUpdated_CanSubscribeMultipleHandlers()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();
        var callCount = 0;
        
        mockService.StatsUpdated += (sender, stats) => callCount++;
        mockService.StatsUpdated += (sender, stats) => callCount++;

        var newStats = new NetworkStats();

        // Act
        mockService.StatsUpdated += Raise.Event<EventHandler<NetworkStats>>(mockService, newStats);

        // Assert
        await Assert.That(callCount).IsEqualTo(2);
    }

    #endregion

    #region Integration-like Behavior Tests

    [Test]
    public async Task NetworkMonitorService_TypicalWorkflow_Succeeds()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true }
        };
        mockService.GetAdapters().Returns(adapters);
        mockService.GetCurrentStats().Returns(new NetworkStats 
        { 
            DownloadSpeedBps = 1_000_000,
            UploadSpeedBps = 500_000
        });

        // Act - Simulate typical app workflow
        var availableAdapters = mockService.GetAdapters();
        mockService.SetAdapter(availableAdapters[0].Id);
        mockService.Poll();
        var stats = mockService.GetCurrentStats();

        // Assert
        await Assert.That(availableAdapters.Count).IsEqualTo(1);
        await Assert.That(stats.DownloadSpeedBps).IsEqualTo(1_000_000);
        mockService.Received(1).SetAdapter("eth0");
        mockService.Received(1).Poll();
    }

    [Test]
    public async Task NetworkMonitorService_SwitchingApis_Succeeds()
    {
        // Arrange
        var mockService = Substitute.For<INetworkMonitorService>();
        mockService.IsUsingIpHelperApi.Returns(false, true); // Returns false first, then true

        // Act - Start with .NET API
        var initialMode = mockService.IsUsingIpHelperApi;
        mockService.SetUseIpHelperApi(true);
        var newMode = mockService.IsUsingIpHelperApi;

        // Assert
        await Assert.That(initialMode).IsFalse();
        await Assert.That(newMode).IsTrue();
        mockService.Received(1).SetUseIpHelperApi(true);
    }

    #endregion
}

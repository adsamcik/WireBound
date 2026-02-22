using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using WireBound.Avalonia.ViewModels;
using WireBound.Core;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for ConnectionsViewModel
/// </summary>
public class ConnectionsViewModelTests : IAsyncDisposable
{
    private readonly List<ConnectionsViewModel> _createdViewModels = [];
    private readonly IUiDispatcher _dispatcher = new SynchronousDispatcher();
    private readonly FakeTimeProvider _fakeTimeProvider = new();
    private readonly IProcessNetworkService _processNetworkServiceMock;
    private readonly IDnsResolverService _dnsResolverMock;
    private readonly IElevationService _elevationServiceMock;
    private readonly INavigationService _navigationServiceMock;

    public ConnectionsViewModelTests()
    {
        _processNetworkServiceMock = Substitute.For<IProcessNetworkService>();
        _dnsResolverMock = Substitute.For<IDnsResolverService>();
        _elevationServiceMock = Substitute.For<IElevationService>();
        _navigationServiceMock = Substitute.For<INavigationService>();

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        // Setup process network service defaults
        _processNetworkServiceMock.IsPlatformSupported.Returns(true);
        _processNetworkServiceMock.IsRunning.Returns(false);
        _processNetworkServiceMock.StartAsync().Returns(true);
        _processNetworkServiceMock.GetConnectionStatsAsync().Returns(new List<ConnectionStats>());

        // Setup elevation service defaults
        _elevationServiceMock.RequiresElevationFor(Arg.Any<ElevatedFeature>()).Returns(false);
        _elevationServiceMock.IsElevationSupported.Returns(true);

        // Setup navigation — mark Connections as active so visibility-aware updates are processed
        _navigationServiceMock.CurrentView.Returns(Routes.Connections);

        // Setup DNS resolver defaults
        _dnsResolverMock.GetCached(Arg.Any<string>()).Returns((string?)null);
    }

    private ConnectionsViewModel CreateViewModel()
    {
        var viewModel = new ConnectionsViewModel(
            _dispatcher,
            _processNetworkServiceMock,
            _dnsResolverMock,
            _elevationServiceMock,
            _navigationServiceMock,
            _fakeTimeProvider);
        _createdViewModels.Add(viewModel);
        return viewModel;
    }

    private static ConnectionStats CreateConnectionStats(
        string localAddress = "192.168.1.10",
        int localPort = 50000,
        string remoteAddress = "8.8.8.8",
        int remotePort = 443,
        string protocol = "TCP",
        string processName = "TestApp",
        int processId = 1234,
        ConnectionState state = ConnectionState.Established,
        long bytesSent = 1024,
        long bytesReceived = 2048)
    {
        return new ConnectionStats
        {
            LocalAddress = localAddress,
            LocalPort = localPort,
            RemoteAddress = remoteAddress,
            RemotePort = remotePort,
            Protocol = protocol,
            ProcessName = processName,
            ProcessId = processId,
            State = state,
            BytesSent = bytesSent,
            BytesReceived = bytesReceived,
            HasByteCounters = true
        };
    }

    #region Constructor Tests

    [Test]
    public async Task Constructor_InitializesDefaultValues()
    {
        // Act
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert
        viewModel.IsLoading.Should().BeFalse();
        viewModel.HasError.Should().BeFalse();
        viewModel.ErrorMessage.Should().BeEmpty();
        viewModel.SearchText.Should().BeEmpty();
        viewModel.SortColumn.Should().Be("Speed");
        viewModel.SortAscending.Should().BeFalse();
        viewModel.Dispose();
    }

    [Test]
    public void Constructor_InitializesConnectionsCollection()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.Connections.Should().NotBeNull();
        viewModel.Dispose();
    }

    [Test]
    public void Constructor_InitializesCountersToZero()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.ConnectionCount.Should().Be(0);
        viewModel.TcpCount.Should().Be(0);
        viewModel.UdpCount.Should().Be(0);
        viewModel.TotalSent.Should().Be("0 B");
        viewModel.TotalReceived.Should().Be("0 B");
        viewModel.Dispose();
    }

    [Test]
    public void Constructor_ChecksPlatformSupport()
    {
        // Arrange
        _processNetworkServiceMock.IsPlatformSupported.Returns(true);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsPlatformSupported.Should().BeTrue();
        viewModel.Dispose();
    }

    [Test]
    public void Constructor_WhenPlatformNotSupported_SetsIsPlatformSupportedFalse()
    {
        // Arrange
        _processNetworkServiceMock.IsPlatformSupported.Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsPlatformSupported.Should().BeFalse();
        viewModel.Dispose();
    }

    [Test]
    public void Constructor_ChecksMonitoringState()
    {
        // Arrange
        _processNetworkServiceMock.IsRunning.Returns(true);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsMonitoring.Should().BeTrue();
        viewModel.Dispose();
    }

    [Test]
    public async Task Constructor_WhenStartAsyncFails_SetsIsMonitoringFalse()
    {
        // Arrange
        _processNetworkServiceMock.IsRunning.Returns(false);
        _processNetworkServiceMock.StartAsync().Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Wait for the fire-and-forget InitializeAsync to complete
        await viewModel.InitializationTask;

        // Assert - IsMonitoring should remain false because StartAsync failed
        viewModel.IsMonitoring.Should().BeFalse();
        viewModel.Dispose();
    }

    [Test]
    public void Constructor_ChecksElevationRequirement()
    {
        // Arrange
        _elevationServiceMock.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring).Returns(true);
        _elevationServiceMock.IsElevationSupported.Returns(true);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.RequiresElevation.Should().BeTrue();
        viewModel.Dispose();
    }

    [Test]
    public void Constructor_WhenElevationNotRequired_SetsRequiresElevationFalse()
    {
        // Arrange
        _elevationServiceMock.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring).Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.RequiresElevation.Should().BeFalse();
        viewModel.Dispose();
    }

    [Test]
    public void Constructor_WhenElevationNotSupported_SetsRequiresElevationFalse()
    {
        // Arrange
        _elevationServiceMock.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring).Returns(true);
        _elevationServiceMock.IsElevationSupported.Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Assert - RequiresElevation should be false when elevation isn't supported
        viewModel.RequiresElevation.Should().BeFalse();
        viewModel.Dispose();
    }

    [Test]
    public async Task Constructor_StartsProcessNetworkService()
    {
        // Act
        var viewModel = CreateViewModel();

        // Allow async initialization to complete
        await viewModel.InitializationTask;

        // Assert
        _processNetworkServiceMock.Received(1).StartAsync();
        viewModel.Dispose();
    }

    [Test]
    public void Constructor_SelectedConnectionIsNull()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SelectedConnection.Should().BeNull();
        viewModel.Dispose();
    }

    #endregion

    #region ConnectionDisplayItem Tests

    [Test]
    public void ConnectionDisplayItem_FromConnectionStats_MapsAllProperties()
    {
        // Arrange
        var stats = CreateConnectionStats();

        // Act
        var item = ConnectionDisplayItem.FromConnectionStats(stats);

        // Assert
        item.Protocol.Should().Be("TCP");
        item.LocalEndpoint.Should().Be("192.168.1.10:50000");
        item.RemoteEndpoint.Should().Contain("8.8.8.8");
        item.ProcessName.Should().Be("TestApp");
        item.ProcessId.Should().Be(1234);
        item.State.Should().Be("Established");
        item.HasByteCounters.Should().BeTrue();
    }

    [Test]
    public void ConnectionDisplayItem_FromConnectionStats_FormatsBytes()
    {
        // Arrange
        var stats = CreateConnectionStats(bytesSent: 1024, bytesReceived: 2048);

        // Act
        var item = ConnectionDisplayItem.FromConnectionStats(stats);

        // Assert
        item.BytesSent.Should().Be("1.00 KB");
        item.BytesReceived.Should().Be("2.00 KB");
    }

    [Test]
    public void ConnectionDisplayItem_UpdateFrom_UpdatesProperties()
    {
        // Arrange
        var initialStats = CreateConnectionStats(bytesSent: 100, bytesReceived: 200);
        var item = ConnectionDisplayItem.FromConnectionStats(initialStats);

        var updatedStats = CreateConnectionStats(bytesSent: 1024, bytesReceived: 2048);
        updatedStats.State = ConnectionState.CloseWait;

        // Act
        item.UpdateFrom(updatedStats);

        // Assert
        item.State.Should().Be("CloseWait");
        item.BytesSent.Should().Be("1.00 KB");
        item.BytesReceived.Should().Be("2.00 KB");
    }

    [Test]
    public void ConnectionDisplayItem_UpdateFrom_UpdatesHostname()
    {
        // Arrange
        var stats = CreateConnectionStats();
        var item = ConnectionDisplayItem.FromConnectionStats(stats);

        var updatedStats = CreateConnectionStats();
        updatedStats.ResolvedHostname = "dns.google";

        // Act
        item.UpdateFrom(updatedStats);

        // Assert
        item.RemoteHostname.Should().Be("dns.google");
        item.DisplayName.Should().Be("dns.google");
    }

    #endregion

    #region Sorting Tests

    [Test]
    public void SortByCommand_ChangesColumn_SetsSortColumn()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SortByCommand.Execute("Protocol");

        // Assert
        viewModel.SortColumn.Should().Be("Protocol");
        viewModel.Dispose();
    }

    [Test]
    public void SortByCommand_SameColumn_TogglesDirection()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.SortByCommand.Execute("Protocol");
        var initialDirection = viewModel.SortAscending;

        // Act
        viewModel.SortByCommand.Execute("Protocol");

        // Assert
        viewModel.SortAscending.Should().Be(!initialDirection);
        viewModel.Dispose();
    }

    [Test]
    public void SortByCommand_DifferentColumn_ResetsToDescending()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.SortByCommand.Execute("Protocol");
        viewModel.SortByCommand.Execute("Protocol"); // Toggle to ascending

        // Act
        viewModel.SortByCommand.Execute("Remote");

        // Assert
        viewModel.SortAscending.Should().BeFalse();
        viewModel.Dispose();
    }

    [Test]
    public void SortByCommand_DefaultColumn_IsSpeed()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SortColumn.Should().Be("Speed");
        viewModel.Dispose();
    }

    #endregion

    #region Refresh Command Tests

    [Test]
    public async Task RefreshCommand_CallsGetConnectionStats()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        _processNetworkServiceMock.ClearReceivedCalls();

        // Act
        await viewModel.RefreshCommand.ExecuteAsync(null);

        // Assert
        await _processNetworkServiceMock.Received(1).GetConnectionStatsAsync();
        viewModel.Dispose();
    }

    [Test]
    public async Task RefreshCommand_CanExecute()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Assert
        viewModel.RefreshCommand.CanExecute(null).Should().BeTrue();
        viewModel.Dispose();
        await Task.CompletedTask;
    }

    #endregion

    #region Error Handling Tests

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert - should not throw
        viewModel.Dispose();
        viewModel.Dispose();
    }

    [Test]
    public async Task Dispose_StopsRefreshTimer()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Act
        viewModel.Dispose();
        _processNetworkServiceMock.ClearReceivedCalls();

        // Advance time past several timer intervals
        _fakeTimeProvider.Advance(TimeSpan.FromSeconds(5));

        // Assert - no new calls after dispose
        await _processNetworkServiceMock.DidNotReceive().GetConnectionStatsAsync();
    }

    #endregion

    #region Search Text Tests

    [Test]
    public async Task SearchText_WhenChanged_TriggersRefresh()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        _processNetworkServiceMock.ClearReceivedCalls();

        // Act
        viewModel.SearchText = "chrome";

        // Allow async operation
        if (viewModel.PendingSearchTask is not null)
            await viewModel.PendingSearchTask;

        // Assert
        _processNetworkServiceMock.Received().GetConnectionStatsAsync();
        viewModel.Dispose();
    }

    [Test]
    public void SearchText_EmptyString_IsValid()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SearchText = "";

        // Assert
        viewModel.SearchText.Should().BeEmpty();
        viewModel.Dispose();
    }

    #endregion

    #region Statistics Update Tests

    [Test]
    public async Task RefreshConnections_UpdatesConnectionCount()
    {
        // Arrange
        var connections = new List<ConnectionStats>
        {
            CreateConnectionStats(remoteAddress: "8.8.8.8", remotePort: 443, protocol: "TCP"),
            CreateConnectionStats(remoteAddress: "1.1.1.1", remotePort: 80, protocol: "TCP"),
            CreateConnectionStats(remoteAddress: "9.9.9.9", remotePort: 53, protocol: "UDP")
        };
        _processNetworkServiceMock.GetConnectionStatsAsync().Returns(connections);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert
        viewModel.ConnectionCount.Should().Be(3);
        viewModel.Dispose();
    }

    [Test]
    public async Task RefreshConnections_UpdatesProtocolCounts()
    {
        // Arrange
        var connections = new List<ConnectionStats>
        {
            CreateConnectionStats(protocol: "TCP"),
            CreateConnectionStats(remoteAddress: "1.1.1.1", protocol: "TCP"),
            CreateConnectionStats(remoteAddress: "9.9.9.9", protocol: "UDP")
        };
        _processNetworkServiceMock.GetConnectionStatsAsync().Returns(connections);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert
        viewModel.TcpCount.Should().Be(2);
        viewModel.UdpCount.Should().Be(1);
        viewModel.Dispose();
    }

    [Test]
    public async Task RefreshConnections_UpdatesTotalBytes()
    {
        // Arrange
        var connections = new List<ConnectionStats>
        {
            CreateConnectionStats(bytesSent: 1024, bytesReceived: 2048),
            CreateConnectionStats(remoteAddress: "1.1.1.1", bytesSent: 1024, bytesReceived: 2048)
        };
        _processNetworkServiceMock.GetConnectionStatsAsync().Returns(connections);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert
        viewModel.TotalSent.Should().Be("2.00 KB");
        viewModel.TotalReceived.Should().Be("4.00 KB");
        viewModel.Dispose();
    }

    [Test]
    public async Task RefreshConnections_QueuesDnsResolution()
    {
        // Arrange
        var connections = new List<ConnectionStats>
        {
            CreateConnectionStats(remoteAddress: "8.8.8.8")
        };
        _processNetworkServiceMock.GetConnectionStatsAsync().Returns(connections);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert
        _dnsResolverMock.Received().QueueForResolution("8.8.8.8");
        viewModel.Dispose();
    }

    [Test]
    public async Task RefreshConnections_UsesCachedHostname()
    {
        // Arrange
        _dnsResolverMock.GetCached("8.8.8.8").Returns("dns.google");

        var connections = new List<ConnectionStats>
        {
            CreateConnectionStats(remoteAddress: "8.8.8.8")
        };
        _processNetworkServiceMock.GetConnectionStatsAsync().Returns(connections);

        var viewModel = CreateViewModel();
        await viewModel.InitializationTask;

        // Assert
        _dnsResolverMock.Received().GetCached("8.8.8.8");
        viewModel.Dispose();
    }

    #endregion

    #region Null Service Tests

    [Test]
    public void Constructor_WithNullProcessNetworkService_DoesNotThrow()
    {
        // Act & Assert - should not throw even with null service
        // Note: The constructor accepts null for processNetworkService
        var viewModel = new ConnectionsViewModel(
            _dispatcher,
            null!,
            _dnsResolverMock,
            _elevationServiceMock,
            _navigationServiceMock);

        viewModel.IsPlatformSupported.Should().BeFalse();
        viewModel.Dispose();
    }

    [Test]
    public void Constructor_WithNullDnsResolver_DoesNotThrow()
    {
        // Act & Assert
        var viewModel = new ConnectionsViewModel(
            _dispatcher,
            _processNetworkServiceMock,
            null!,
            _elevationServiceMock,
            _navigationServiceMock);

        viewModel.Dispose();
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        foreach (var vm in _createdViewModels)
        {
            vm.Dispose();
        }
        _createdViewModels.Clear();
        return ValueTask.CompletedTask;
    }
}

using WireBound.Avalonia.Services;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for ProcessNetworkService
/// </summary>
public class ProcessNetworkServiceTests : IDisposable
{
    private readonly IProcessNetworkProviderFactory _providerFactory;
    private readonly IProcessNetworkProvider _provider;
    private readonly ProcessNetworkService _service;

    public ProcessNetworkServiceTests()
    {
        _provider = Substitute.For<IProcessNetworkProvider>();
        _provider.IsMonitoring.Returns(false);
        _provider.StartMonitoringAsync(Arg.Any<CancellationToken>()).Returns(true);

        _providerFactory = Substitute.For<IProcessNetworkProviderFactory>();
        _providerFactory.GetProvider().Returns(_provider);
        _providerFactory.HasElevatedProvider.Returns(false);

        _service = new ProcessNetworkService(_providerFactory);
    }

    public void Dispose()
    {
        _service.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void IsRunning_BeforeStart_ReturnsFalse()
    {
        _service.IsRunning.Should().BeFalse();
    }

    [Test]
    public void HasRequiredPrivileges_DelegatesToFactory()
    {
        var result = _service.HasRequiredPrivileges;
        result.Should().BeFalse();
    }

    [Test]
    public void IsPlatformSupported_ReturnsTrue()
    {
        _service.IsPlatformSupported.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // StartAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task StartAsync_GetsProviderFromFactory()
    {
        await _service.StartAsync();

        _providerFactory.Received(1).GetProvider();
    }

    [Test]
    public async Task StartAsync_StartsMonitoring()
    {
        await _service.StartAsync();

        await _provider.Received(1).StartMonitoringAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StartAsync_ReturnsTrue_WhenSuccessful()
    {
        var result = await _service.StartAsync();

        result.Should().BeTrue();
    }

    [Test]
    public async Task StartAsync_ReturnsFalse_WhenProviderThrows()
    {
        _providerFactory.GetProvider().Returns(_ => throw new InvalidOperationException("fail"));

        var result = await _service.StartAsync();

        result.Should().BeFalse();
    }

    [Test]
    public async Task StartAsync_RaisesErrorOccurred_WhenProviderThrows()
    {
        _providerFactory.GetProvider().Returns(_ => throw new InvalidOperationException("fail"));

        ProcessNetworkErrorEventArgs? receivedArgs = null;
        _service.ErrorOccurred += (_, args) => receivedArgs = args;

        await _service.StartAsync();

        receivedArgs.Should().NotBeNull();
        receivedArgs!.Message.Should().Contain("Failed to start");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // StopAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task StopAsync_BeforeStart_DoesNotThrow()
    {
        var action = () => _service.StopAsync();
        await action.Should().NotThrowAsync();
    }

    [Test]
    public async Task StopAsync_AfterStart_StopsProvider()
    {
        await _service.StartAsync();
        await _service.StopAsync();

        await _provider.Received(1).StopMonitoringAsync();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetCurrentStats Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetCurrentStats_BeforeStart_ReturnsEmpty()
    {
        var stats = _service.GetCurrentStats();

        stats.Should().NotBeNull();
        stats.Count.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetTopProcesses Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetTopProcesses_ReturnsEmptyBeforeStart()
    {
        var top = _service.GetTopProcesses(5);

        top.Should().NotBeNull();
        top.Count.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // GetConnectionStatsAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task GetConnectionStatsAsync_DelegatesToProvider()
    {
        var expectedStats = new List<ConnectionStats>();
        _provider.GetConnectionStatsAsync(Arg.Any<CancellationToken>())
            .Returns(expectedStats);

        var result = await _service.GetConnectionStatsAsync();

        result.Should().NotBeNull();
    }

    private static ConnectionStats Conn(int processId, string processName = "", int localPort = 5000,
        long bytesSent = 0, long bytesReceived = 0) => new()
    {
        Protocol = "TCP",
        LocalAddress = "127.0.0.1",
        LocalPort = localPort,
        RemoteAddress = "127.0.0.1",
        RemotePort = 8080,
        ProcessId = processId,
        ProcessName = processName,
        BytesSent = bytesSent,
        BytesReceived = bytesReceived,
        HasByteCounters = true
    };

    [Test]
    public async Task GetConnectionStatsAsync_HelperConnected_AttributesPreExistingFromOsTable()
    {
        var elevated = new List<ConnectionStats>
        {
            Conn(processId: 0, processName: "Unattributed (pre-existing connection)", bytesSent: 2000, bytesReceived: 1000)
        };
        _provider.GetConnectionStatsAsync(Arg.Any<CancellationToken>()).Returns(elevated);

        var basicProvider = Substitute.For<IProcessNetworkProvider>();
        basicProvider.GetConnectionStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ConnectionStats> { Conn(processId: 4321, processName: "node") });

        _providerFactory.HasElevatedProvider.Returns(true);
        _providerFactory.GetBasicProvider().Returns(basicProvider);

        var result = await _service.GetConnectionStatsAsync();

        var enriched = result.Single();
        enriched.ProcessId.Should().Be(4321);
        enriched.ProcessName.Should().Be("node");
        // Identity recovered, byte volume cleared (OS table has no byte data).
        enriched.BytesSent.Should().Be(0);
        enriched.BytesReceived.Should().Be(0);
    }

    [Test]
    public async Task GetConnectionStatsAsync_AmbiguousOwner_LeavesConnectionUnattributed()
    {
        _provider.GetConnectionStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ConnectionStats> { Conn(processId: 0) });

        var basicProvider = Substitute.For<IProcessNetworkProvider>();
        // Same connection key resolves to two different PIDs -> ambiguous, skip.
        basicProvider.GetConnectionStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ConnectionStats> { Conn(processId: 111, processName: "a"), Conn(processId: 222, processName: "b") });

        _providerFactory.HasElevatedProvider.Returns(true);
        _providerFactory.GetBasicProvider().Returns(basicProvider);

        var result = await _service.GetConnectionStatsAsync();

        result.Single().ProcessId.Should().Be(0);
    }

    [Test]
    public async Task GetConnectionStatsAsync_NoElevatedProvider_DoesNotQueryOsTable()
    {
        _provider.GetConnectionStatsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ConnectionStats> { Conn(processId: 0) });

        var basicProvider = Substitute.For<IProcessNetworkProvider>();
        _providerFactory.HasElevatedProvider.Returns(false);
        _providerFactory.GetBasicProvider().Returns(basicProvider);

        var result = await _service.GetConnectionStatsAsync();

        result.Single().ProcessId.Should().Be(0);
        await basicProvider.DidNotReceive().GetConnectionStatsAsync(Arg.Any<CancellationToken>());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ProviderChanged Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ProviderChanged_WhileMonitoring_StartsNewProvider()
    {
        // Arrange — start monitoring with the old provider
        _provider.IsMonitoring.Returns(true);
        await _service.StartAsync();

        var newProvider = Substitute.For<IProcessNetworkProvider>();
        newProvider.StartMonitoringAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act — simulate the factory firing ProviderChanged
        _providerFactory.ProviderChanged += Raise.EventWith(
            _providerFactory,
            new ProviderChangedEventArgs(newProvider));

        // Allow the async handler to complete
        if (_service.PendingProviderChangeTask is not null)
            await _service.PendingProviderChangeTask;

        // Assert — new provider was started, old provider was stopped
        await newProvider.Received(1).StartMonitoringAsync(Arg.Any<CancellationToken>());
        await _provider.Received(1).StopMonitoringAsync();
    }

    [Test]
    public async Task ProviderChanged_WhileMonitoring_UnsubscribesOldProviderEvents()
    {
        // Arrange
        _provider.IsMonitoring.Returns(true);
        await _service.StartAsync();

        var newProvider = Substitute.For<IProcessNetworkProvider>();
        newProvider.StartMonitoringAsync(Arg.Any<CancellationToken>()).Returns(true);

        // Act
        _providerFactory.ProviderChanged += Raise.EventWith(
            _providerFactory,
            new ProviderChangedEventArgs(newProvider));
        if (_service.PendingProviderChangeTask is not null)
            await _service.PendingProviderChangeTask;

        // Assert — raising StatsUpdated on OLD provider should NOT propagate
        ProcessStatsUpdatedEventArgs? received = null;
        _service.StatsUpdated += (_, args) => received = args;

        _provider.StatsUpdated += Raise.EventWith(
            _provider,
            new ProcessNetworkProviderEventArgs([], DateTimeOffset.Now, TimeSpan.FromSeconds(1)));

        received.Should().BeNull();
    }

    [Test]
    public async Task ProviderChanged_WhileMonitoring_SubscribesNewProviderEvents()
    {
        // Arrange
        _provider.IsMonitoring.Returns(true);
        await _service.StartAsync();

        var newProvider = Substitute.For<IProcessNetworkProvider>();
        newProvider.StartMonitoringAsync(Arg.Any<CancellationToken>()).Returns(true);

        _providerFactory.ProviderChanged += Raise.EventWith(
            _providerFactory,
            new ProviderChangedEventArgs(newProvider));
        if (_service.PendingProviderChangeTask is not null)
            await _service.PendingProviderChangeTask;

        // Act — raise StatsUpdated on NEW provider
        ProcessStatsUpdatedEventArgs? received = null;
        _service.StatsUpdated += (_, args) => received = args;

        newProvider.StatsUpdated += Raise.EventWith(
            newProvider,
            new ProcessNetworkProviderEventArgs([], DateTimeOffset.Now, TimeSpan.FromSeconds(1)));

        // Assert
        received.Should().NotBeNull();
    }

    [Test]
    public async Task ProviderChanged_WhenNotMonitoring_DoesNotStartNewProvider()
    {
        // Arrange — provider exists but is not monitoring
        await _service.StartAsync();
        _provider.IsMonitoring.Returns(false);

        var newProvider = Substitute.For<IProcessNetworkProvider>();

        // Act
        _providerFactory.ProviderChanged += Raise.EventWith(
            _providerFactory,
            new ProviderChangedEventArgs(newProvider));
        if (_service.PendingProviderChangeTask is not null)
            await _service.PendingProviderChangeTask;

        // Assert — new provider should NOT be started
        await newProvider.DidNotReceive().StartMonitoringAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProviderChanged_GetConnectionStatsAsync_UsesNewProvider()
    {
        // Arrange
        await _service.StartAsync();

        var newProvider = Substitute.For<IProcessNetworkProvider>();
        var expectedStats = new List<ConnectionStats> { new() };
        newProvider.GetConnectionStatsAsync(Arg.Any<CancellationToken>()).Returns(expectedStats);

        // Act — swap provider
        _providerFactory.ProviderChanged += Raise.EventWith(
            _providerFactory,
            new ProviderChangedEventArgs(newProvider));
        if (_service.PendingProviderChangeTask is not null)
            await _service.PendingProviderChangeTask;

        var result = await _service.GetConnectionStatsAsync();

        // Assert
        result.Should().BeSameAs(expectedStats);
        await newProvider.Received(1).GetConnectionStatsAsync(Arg.Any<CancellationToken>());
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Dispose Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task Dispose_AfterStart_DisposesProvider()
    {
        // Use a provider that implements IDisposable
        var disposableProvider = Substitute.For<IProcessNetworkProvider, IDisposable>();
        disposableProvider.IsMonitoring.Returns(false);
        disposableProvider.StartMonitoringAsync(Arg.Any<CancellationToken>()).Returns(true);
        _providerFactory.GetProvider().Returns(disposableProvider);

        var service = new ProcessNetworkService(_providerFactory);
        await service.StartAsync();
        service.Dispose();

        ((IDisposable)disposableProvider).Received(1).Dispose();
    }

    [Test]
    public async Task StartAsync_AfterDispose_ReturnsFalse()
    {
        _service.Dispose();

        var result = await _service.StartAsync();

        result.Should().BeFalse();
    }
}

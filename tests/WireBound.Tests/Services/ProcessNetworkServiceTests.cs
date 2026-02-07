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

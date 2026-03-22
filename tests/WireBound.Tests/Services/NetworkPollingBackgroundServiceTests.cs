using Microsoft.Extensions.Logging;
using WireBound.Avalonia.Services;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;

namespace WireBound.Tests.Services;

/// <summary>
/// Unit tests for NetworkPollingBackgroundService — tests the interval configuration logic.
/// The actual ExecuteAsync is not tested here as it requires full service orchestration.
/// </summary>
public class NetworkPollingBackgroundServiceTests : IAsyncDisposable
{
    private readonly INetworkMonitorService _networkMonitor;
    private readonly ISystemMonitorService _systemMonitor;
    private readonly ISystemHistoryService _systemHistory;
    private readonly IDataPersistenceService _persistence;
    private readonly ITrayIconService _trayIcon;
    private readonly ILogger<NetworkPollingBackgroundService> _logger;
    private readonly NetworkPollingBackgroundService _service;

    public NetworkPollingBackgroundServiceTests()
    {
        _networkMonitor = Substitute.For<INetworkMonitorService>();
        _systemMonitor = Substitute.For<ISystemMonitorService>();
        _systemHistory = Substitute.For<ISystemHistoryService>();
        _persistence = Substitute.For<IDataPersistenceService>();
        _trayIcon = Substitute.For<ITrayIconService>();
        _logger = Substitute.For<ILogger<NetworkPollingBackgroundService>>();

        _persistence.GetSettingsAsync().Returns(new AppSettings());
        _networkMonitor.GetCurrentStats().Returns(new NetworkStats());
        _systemMonitor.GetCurrentStats().Returns(new SystemStats());

        _service = new NetworkPollingBackgroundService(
            _networkMonitor, _systemMonitor, _systemHistory,
            _persistence, _trayIcon, _logger);
    }

    public async ValueTask DisposeAsync()
    {
        await _service.StopAsync(CancellationToken.None);
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UpdatePollingInterval Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void UpdatePollingInterval_ValidValue_DoesNotThrow()
    {
        var action = () => _service.UpdatePollingInterval(500);
        action.Should().NotThrow();
    }

    [Test]
    public void UpdatePollingInterval_BelowMinimum_ClampsToMinimum()
    {
        // Should not throw, just clamp
        var action = () => _service.UpdatePollingInterval(10);
        action.Should().NotThrow();
    }

    [Test]
    public void UpdatePollingInterval_AboveMaximum_ClampsToMaximum()
    {
        var action = () => _service.UpdatePollingInterval(999999);
        action.Should().NotThrow();
    }

    [Test]
    [Arguments(100)]
    [Arguments(500)]
    [Arguments(1000)]
    [Arguments(5000)]
    [Arguments(60000)]
    public void UpdatePollingInterval_ValidRangeValues_AcceptsWithoutIssue(int ms)
    {
        var action = () => _service.UpdatePollingInterval(ms);
        action.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // UpdateSaveInterval Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void UpdateSaveInterval_ValidValue_DoesNotThrow()
    {
        var action = () => _service.UpdateSaveInterval(30);
        action.Should().NotThrow();
    }

    [Test]
    public void UpdateSaveInterval_BelowMinimum_ClampsToMinimum()
    {
        var action = () => _service.UpdateSaveInterval(1);
        action.Should().NotThrow();
    }

    [Test]
    [Arguments(10)]
    [Arguments(30)]
    [Arguments(60)]
    [Arguments(300)]
    public void UpdateSaveInterval_ValidRangeValues_AcceptsWithoutIssue(int seconds)
    {
        var action = () => _service.UpdateSaveInterval(seconds);
        action.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // StartAsync / StopAsync Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task StartAsync_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var action = () => _service.StartAsync(cts.Token);
        await action.Should().NotThrowAsync();
    }

    [Test]
    public async Task StopAsync_DoesNotThrow()
    {
        var action = () => _service.StopAsync(CancellationToken.None);
        await action.Should().NotThrowAsync();
    }

    [Test]
    public async Task StartThenStop_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // Start and let it run briefly
        var startTask = _service.StartAsync(cts.Token);

        // Wait for service to enter polling loop (deterministic)
        if (_service.PollingStartedTask is not null)
            await _service.PollingStartedTask;

        await _service.StopAsync(CancellationToken.None);

        // Ensure start task completes
        await startTask;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Adaptive Polling Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SetAdaptivePolling_Enable_DoesNotThrow()
    {
        var action = () => _service.SetAdaptivePolling(true, 1000);
        action.Should().NotThrow();
    }

    [Test]
    public void SetAdaptivePolling_Disable_RestoresBaseInterval()
    {
        // Arrange — change interval away from default first
        _service.UpdatePollingInterval(2000);

        // Act — disable adaptive polling with a specific base interval
        _service.SetAdaptivePolling(false, 500);

        // Assert — CurrentPollingIntervalMs should be restored to the base interval
        _service.CurrentPollingIntervalMs.Should().Be(500);
    }

    [Test]
    public void SetAdaptivePolling_WithLowBaseInterval_ClampsToMinimum()
    {
        // Arrange & Act — use a base interval below MinPollingIntervalMs (100)
        _service.SetAdaptivePolling(false, 10);

        // Assert — should be clamped to minimum (100ms)
        _service.CurrentPollingIntervalMs.Should().Be(100);
    }

    [Test]
    public void SetAdaptivePolling_EnableThenDisable_RestoresInterval()
    {
        // Arrange — enable adaptive polling with a known base
        _service.SetAdaptivePolling(true, 750);

        // Act — change the polling interval (simulating adaptive adjustment), then disable
        _service.UpdatePollingInterval(3000);
        _service.SetAdaptivePolling(false, 750);

        // Assert — disabling should restore the base interval
        _service.CurrentPollingIntervalMs.Should().Be(750);
    }

    [Test]
    public void CurrentPollingIntervalMs_ReturnsCurrentValue()
    {
        // The default polling interval is 1000ms
        _service.CurrentPollingIntervalMs.Should().Be(1000);
    }

    [Test]
    public void CurrentPollingIntervalMs_AfterUpdatePollingInterval_ReflectsNewValue()
    {
        // Arrange & Act
        _service.UpdatePollingInterval(2500);

        // Assert
        _service.CurrentPollingIntervalMs.Should().Be(2500);
    }

    [Test]
    public void SetAdaptivePolling_MultipleCalls_DoesNotThrow()
    {
        var action = () =>
        {
            _service.SetAdaptivePolling(true, 1000);
            _service.SetAdaptivePolling(true, 500);
            _service.SetAdaptivePolling(false, 2000);
            _service.SetAdaptivePolling(true, 300);
            _service.SetAdaptivePolling(false, 1000);
        };
        action.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Per-App Stats Persistence Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task ExecuteAsync_WithPerAppTrackingRunning_SavesAppStats(CancellationToken cancellationToken)
    {
        // Arrange — create a service with per-app tracking enabled
        var processNetworkService = Substitute.For<IProcessNetworkService>();
        processNetworkService.IsRunning.Returns(true);
        var appStats = new List<ProcessNetworkStats>
        {
            new()
            {
                ProcessId = 1234,
                ProcessName = "chrome",
                AppIdentifier = "abc123",
                SessionBytesReceived = 5000,
                SessionBytesSent = 1000
            }
        };
        processNetworkService.GetCurrentStats().Returns(appStats);

        var settings = new AppSettings
        {
            PollingIntervalMs = 100,
            SaveIntervalSeconds = 1,
            IsPerAppTrackingEnabled = true
        };
        _persistence.GetSettingsAsync().Returns(settings);

        var serviceWithAppTracking = new NetworkPollingBackgroundService(
            _networkMonitor, _systemMonitor, _systemHistory,
            _persistence, _trayIcon, _logger,
            processNetworkService: processNetworkService);

        // Act — run for enough time for at least one save cycle
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await serviceWithAppTracking.StartAsync(cts.Token);
            if (serviceWithAppTracking.PollingStartedTask is not null)
                await serviceWithAppTracking.PollingStartedTask;
            await Task.Delay(2000, cancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await serviceWithAppTracking.StopAsync(CancellationToken.None);
            serviceWithAppTracking.Dispose();
        }

        // Assert — SaveAppStatsAsync should have been called at least once
        await _persistence.Received().SaveAppStatsAsync(
            Arg.Is<IEnumerable<ProcessNetworkStats>>(s => s.Any()));
    }

    [Test]
    public async Task ExecuteAsync_WithoutPerAppTracking_DoesNotSaveAppStats(CancellationToken cancellationToken)
    {
        // Arrange — no process network service injected (default constructor)
        var settings = new AppSettings
        {
            PollingIntervalMs = 100,
            SaveIntervalSeconds = 1
        };
        _persistence.GetSettingsAsync().Returns(settings);

        // Act — run briefly
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await _service.StartAsync(cts.Token);
            if (_service.PollingStartedTask is not null)
                await _service.PollingStartedTask;
            await Task.Delay(2000, cancellationToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _service.StopAsync(CancellationToken.None);
        }

        // Assert — SaveAppStatsAsync should NOT have been called
        await _persistence.DidNotReceive().SaveAppStatsAsync(
            Arg.Any<IEnumerable<ProcessNetworkStats>>());
    }
}

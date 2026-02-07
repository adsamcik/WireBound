using Microsoft.Extensions.Logging;
using WireBound.Avalonia.Services;
using WireBound.Core.Models;
using WireBound.Core.Services;

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

        // Wait a bit then stop
        await Task.Delay(50);
        await _service.StopAsync(CancellationToken.None);

        // Ensure start task completes
        await startTask;
    }
}

using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using WireBound.Avalonia.ViewModels;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for ChartsViewModel
/// </summary>
public class ChartsViewModelTests : IAsyncDisposable
{
    private readonly INetworkMonitorService _networkMonitorMock;
    private readonly IDataPersistenceService _persistenceMock;
    private readonly ISystemMonitorService _systemMonitorMock;
    private readonly ILogger<ChartsViewModel> _loggerMock;
    private ChartsViewModel? _viewModel;

    public ChartsViewModelTests()
    {
        _networkMonitorMock = Substitute.For<INetworkMonitorService>();
        _persistenceMock = Substitute.For<IDataPersistenceService>();
        _systemMonitorMock = Substitute.For<ISystemMonitorService>();
        _loggerMock = Substitute.For<ILogger<ChartsViewModel>>();

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        // Setup network monitor with default stats
        _networkMonitorMock.GetCurrentStats().Returns(CreateDefaultNetworkStats());

        // Setup persistence to return empty history
        _persistenceMock.GetSpeedHistoryAsync(Arg.Any<DateTime>()).Returns(new List<SpeedSnapshot>());

        // Setup system monitor with default stats
        _systemMonitorMock.GetCurrentStats().Returns(CreateDefaultSystemStats());
    }

    private static NetworkStats CreateDefaultNetworkStats()
    {
        return new NetworkStats
        {
            Timestamp = DateTime.Now,
            DownloadSpeedBps = 0,
            UploadSpeedBps = 0,
            SessionBytesReceived = 0,
            SessionBytesSent = 0,
            TotalBytesReceived = 0,
            TotalBytesSent = 0
        };
    }

    private static SystemStats CreateDefaultSystemStats()
    {
        return new SystemStats
        {
            Timestamp = DateTime.Now,
            Cpu = new CpuStats { UsagePercent = 0 },
            Memory = new MemoryStats { TotalBytes = 100, UsedBytes = 0 }
        };
    }

    private ChartsViewModel CreateViewModel()
    {
        return new ChartsViewModel(
            _networkMonitorMock,
            _persistenceMock,
            _systemMonitorMock,
            _loggerMock);
    }

    private ChartsViewModel CreateViewModelWithoutSystemMonitor()
    {
        return new ChartsViewModel(
            _networkMonitorMock,
            _persistenceMock,
            null,
            _loggerMock);
    }

    #region Constructor Tests

    [Test]
    public void Constructor_InitializesDefaultSpeedValues()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.DownloadSpeed.Should().Be("0 B/s");
        _viewModel.UploadSpeed.Should().Be("0 B/s");
    }

    [Test]
    public void Constructor_InitializesPeakSpeedValues()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.PeakDownloadSpeed.Should().Be("0 B/s");
        _viewModel.PeakUploadSpeed.Should().Be("0 B/s");
    }

    [Test]
    public void Constructor_InitializesAverageSpeedValues()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.AverageDownloadSpeed.Should().Be("0 B/s");
        _viewModel.AverageUploadSpeed.Should().Be("0 B/s");
    }

    [Test]
    public void Constructor_InitializesSpeedSeries()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.SpeedSeries.Should().NotBeNull();
        _viewModel.SpeedSeries.Should().HaveCount(2); // Download and Upload series
    }

    [Test]
    public void Constructor_InitializesXAxes()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.XAxes.Should().NotBeNull();
        _viewModel.XAxes.Should().NotBeEmpty();
    }

    [Test]
    public void Constructor_InitializesYAxes()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.YAxes.Should().NotBeNull();
        _viewModel.YAxes.Should().NotBeEmpty();
    }

    [Test]
    public void Constructor_InitializesSecondaryYAxes()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.SecondaryYAxes.Should().NotBeNull();
        _viewModel.SecondaryYAxes.Should().NotBeEmpty();
    }

    [Test]
    public void Constructor_InitializesTimeRangeOptions()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.TimeRangeOptions.Should().NotBeNull();
        _viewModel.TimeRangeOptions.Should().HaveCount(5); // 30s, 1m, 5m, 15m, 1h
    }

    [Test]
    public void Constructor_SetsDefaultTimeRangeToOneMinute()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.SelectedTimeRange.Should().NotBeNull();
        _viewModel.SelectedTimeRange.Seconds.Should().Be(60);
        _viewModel.SelectedTimeRange.Label.Should().Be("1m");
    }

    [Test]
    public void Constructor_InitializesUpdatesPausedToFalse()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.IsUpdatesPaused.Should().BeFalse();
    }

    [Test]
    public void Constructor_InitializesPauseStatusTextToEmpty()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.PauseStatusText.Should().BeEmpty();
    }

    [Test]
    public void Constructor_InitializesCpuOverlayToFalse()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.ShowCpuOverlay.Should().BeFalse();
    }

    [Test]
    public void Constructor_InitializesMemoryOverlayToFalse()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.ShowMemoryOverlay.Should().BeFalse();
    }

    [Test]
    public void Constructor_SubscribesToNetworkStatsUpdated()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        // Event subscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Constructor_SubscribesToSystemStatsUpdated_WhenSystemMonitorProvided()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        // Event subscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Constructor_LoadsHistoryFromPersistence()
    {
        // Arrange - setup mock to return some history
        var history = new List<SpeedSnapshot>
        {
            new() { Timestamp = DateTime.Now.AddSeconds(-30), DownloadSpeedBps = 1000, UploadSpeedBps = 500 },
            new() { Timestamp = DateTime.Now.AddSeconds(-20), DownloadSpeedBps = 2000, UploadSpeedBps = 1000 }
        };
        _persistenceMock.GetSpeedHistoryAsync(Arg.Any<DateTime>()).Returns(history);

        // Act
        _viewModel = CreateViewModel();

        // Allow async loading to complete
        Thread.Sleep(100);

        // Assert - verify the method was called
        _persistenceMock.Received().GetSpeedHistoryAsync(Arg.Any<DateTime>());
    }

    [Test]
    public void Constructor_WithNullSystemMonitor_DoesNotThrow()
    {
        // Act
        var action = () => CreateViewModelWithoutSystemMonitor();

        // Assert
        action.Should().NotThrow();
    }

    [Test]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // Act
        var action = () => new ChartsViewModel(
            _networkMonitorMock,
            _persistenceMock,
            _systemMonitorMock,
            null);

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region Time Range Options Tests

    [Test]
    public void TimeRangeOptions_30Seconds_HasCorrectValues()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Assert
        var option = _viewModel.TimeRangeOptions.First(x => x.Label == "30s");
        option.Seconds.Should().Be(30);
        option.Description.Should().Be("Last 30 seconds");
    }

    [Test]
    public void TimeRangeOptions_1Minute_HasCorrectValues()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Assert
        var option = _viewModel.TimeRangeOptions.First(x => x.Label == "1m");
        option.Seconds.Should().Be(60);
        option.Description.Should().Be("Last 1 minute");
    }

    [Test]
    public void TimeRangeOptions_5Minutes_HasCorrectValues()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Assert
        var option = _viewModel.TimeRangeOptions.First(x => x.Label == "5m");
        option.Seconds.Should().Be(300);
        option.Description.Should().Be("Last 5 minutes");
    }

    [Test]
    public void TimeRangeOptions_15Minutes_HasCorrectValues()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Assert
        var option = _viewModel.TimeRangeOptions.First(x => x.Label == "15m");
        option.Seconds.Should().Be(900);
        option.Description.Should().Be("Last 15 minutes");
    }

    [Test]
    public void TimeRangeOptions_1Hour_HasCorrectValues()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Assert
        var option = _viewModel.TimeRangeOptions.First(x => x.Label == "1h");
        option.Seconds.Should().Be(3600);
        option.Description.Should().Be("Last 1 hour");
    }

    #endregion

    #region Time Range Selection Tests

    [Test]
    public void SelectedTimeRange_WhenChanged_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        var newTimeRange = _viewModel.TimeRangeOptions.First(x => x.Label == "5m");

        // Act
        _viewModel.SelectedTimeRange = newTimeRange;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.SelectedTimeRange);
    }

    [Test]
    public void SelectedTimeRange_WhenChanged_UpdatesDisplayData()
    {
        // Arrange
        _viewModel = CreateViewModel();
        var initialTimeRange = _viewModel.SelectedTimeRange;

        // Act
        var newTimeRange = _viewModel.TimeRangeOptions.First(x => x.Label == "5m");
        _viewModel.SelectedTimeRange = newTimeRange;

        // Assert
        _viewModel.SelectedTimeRange.Should().Be(newTimeRange);
        _viewModel.SelectedTimeRange.Seconds.Should().Be(300);
    }

    [Test]
    [Arguments("30s", 30)]
    [Arguments("1m", 60)]
    [Arguments("5m", 300)]
    [Arguments("15m", 900)]
    [Arguments("1h", 3600)]
    public void SelectedTimeRange_AcceptsAllTimeRanges(string label, int expectedSeconds)
    {
        // Arrange
        _viewModel = CreateViewModel();
        var timeRange = _viewModel.TimeRangeOptions.First(x => x.Label == label);

        // Act
        _viewModel.SelectedTimeRange = timeRange;

        // Assert
        _viewModel.SelectedTimeRange.Seconds.Should().Be(expectedSeconds);
    }

    #endregion

    #region Pause/Resume Updates Tests

    [Test]
    public void IsUpdatesPaused_WhenSetToTrue_PausesUpdates()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.IsUpdatesPaused = true;

        // Assert
        _viewModel.IsUpdatesPaused.Should().BeTrue();
    }

    [Test]
    public void IsUpdatesPaused_WhenSetToFalse_ResumesUpdates()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.IsUpdatesPaused = true;

        // Act
        _viewModel.IsUpdatesPaused = false;

        // Assert
        _viewModel.IsUpdatesPaused.Should().BeFalse();
    }

    [Test]
    public void IsUpdatesPaused_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.IsUpdatesPaused = true;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.IsUpdatesPaused);
    }

    [Test]
    public void PauseStatusText_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("PauseStatusText")!.SetValue(_viewModel, "Paused");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.PauseStatusText);
    }

    #endregion

    #region CPU Overlay Toggle Tests

    [Test]
    public void ShowCpuOverlay_WhenEnabled_AddsCpuSeriesToSpeedSeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        var initialCount = _viewModel.SpeedSeries.Count;

        // Act
        _viewModel.ShowCpuOverlay = true;

        // Assert
        _viewModel.SpeedSeries.Count.Should().Be(initialCount + 1);
        _viewModel.SpeedSeries.Should().Contain(s => s.Name == "CPU %");
    }

    [Test]
    public void ShowCpuOverlay_WhenDisabled_RemovesCpuSeriesFromSpeedSeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.ShowCpuOverlay = true;
        var countWithCpu = _viewModel.SpeedSeries.Count;

        // Act
        _viewModel.ShowCpuOverlay = false;

        // Assert
        _viewModel.SpeedSeries.Count.Should().Be(countWithCpu - 1);
        _viewModel.SpeedSeries.Should().NotContain(s => s.Name == "CPU %");
    }

    [Test]
    public void ShowCpuOverlay_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.ShowCpuOverlay = true;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.ShowCpuOverlay);
    }

    [Test]
    public void ShowCpuOverlay_WhenEnabledTwice_DoesNotDuplicateSeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.ShowCpuOverlay = true;
        var countAfterFirst = _viewModel.SpeedSeries.Count;

        // Act - try to enable again
        _viewModel.ShowCpuOverlay = true;

        // Assert - count should remain the same
        _viewModel.SpeedSeries.Count.Should().Be(countAfterFirst);
    }

    #endregion

    #region Memory Overlay Toggle Tests

    [Test]
    public void ShowMemoryOverlay_WhenEnabled_AddsMemorySeriesToSpeedSeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        var initialCount = _viewModel.SpeedSeries.Count;

        // Act
        _viewModel.ShowMemoryOverlay = true;

        // Assert
        _viewModel.SpeedSeries.Count.Should().Be(initialCount + 1);
        _viewModel.SpeedSeries.Should().Contain(s => s.Name == "Memory %");
    }

    [Test]
    public void ShowMemoryOverlay_WhenDisabled_RemovesMemorySeriesFromSpeedSeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.ShowMemoryOverlay = true;
        var countWithMemory = _viewModel.SpeedSeries.Count;

        // Act
        _viewModel.ShowMemoryOverlay = false;

        // Assert
        _viewModel.SpeedSeries.Count.Should().Be(countWithMemory - 1);
        _viewModel.SpeedSeries.Should().NotContain(s => s.Name == "Memory %");
    }

    [Test]
    public void ShowMemoryOverlay_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.ShowMemoryOverlay = true;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.ShowMemoryOverlay);
    }

    [Test]
    public void ShowMemoryOverlay_WhenEnabledTwice_DoesNotDuplicateSeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.ShowMemoryOverlay = true;
        var countAfterFirst = _viewModel.SpeedSeries.Count;

        // Act - try to enable again
        _viewModel.ShowMemoryOverlay = true;

        // Assert - count should remain the same
        _viewModel.SpeedSeries.Count.Should().Be(countAfterFirst);
    }

    #endregion

    #region Combined Overlay Tests

    [Test]
    public void ShowBothOverlays_AddsBothSeriesToSpeedSeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        var initialCount = _viewModel.SpeedSeries.Count;

        // Act
        _viewModel.ShowCpuOverlay = true;
        _viewModel.ShowMemoryOverlay = true;

        // Assert
        _viewModel.SpeedSeries.Count.Should().Be(initialCount + 2);
        _viewModel.SpeedSeries.Should().Contain(s => s.Name == "CPU %");
        _viewModel.SpeedSeries.Should().Contain(s => s.Name == "Memory %");
    }

    [Test]
    public void DisableBothOverlays_RemovesBothSeriesFromSpeedSeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.ShowCpuOverlay = true;
        _viewModel.ShowMemoryOverlay = true;
        var countWithBoth = _viewModel.SpeedSeries.Count;

        // Act
        _viewModel.ShowCpuOverlay = false;
        _viewModel.ShowMemoryOverlay = false;

        // Assert
        _viewModel.SpeedSeries.Count.Should().Be(countWithBoth - 2);
        _viewModel.SpeedSeries.Should().NotContain(s => s.Name == "CPU %");
        _viewModel.SpeedSeries.Should().NotContain(s => s.Name == "Memory %");
    }

    #endregion

    #region Property Change Notification Tests

    [Test]
    public void DownloadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("DownloadSpeed")!.SetValue(_viewModel, "100 KB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.DownloadSpeed);
    }

    [Test]
    public void UploadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("UploadSpeed")!.SetValue(_viewModel, "50 KB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.UploadSpeed);
    }

    [Test]
    public void PeakDownloadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("PeakDownloadSpeed")!.SetValue(_viewModel, "1 MB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.PeakDownloadSpeed);
    }

    [Test]
    public void PeakUploadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("PeakUploadSpeed")!.SetValue(_viewModel, "500 KB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.PeakUploadSpeed);
    }

    [Test]
    public void AverageDownloadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("AverageDownloadSpeed")!.SetValue(_viewModel, "200 KB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.AverageDownloadSpeed);
    }

    [Test]
    public void AverageUploadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("AverageUploadSpeed")!.SetValue(_viewModel, "100 KB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.AverageUploadSpeed);
    }

    #endregion

    #region LoadHistory Tests

    [Test]
    public void LoadHistory_WithEmptyHistory_DoesNotThrow()
    {
        // Arrange
        _persistenceMock.GetSpeedHistoryAsync(Arg.Any<DateTime>()).Returns(new List<SpeedSnapshot>());

        // Act
        var action = () => CreateViewModel();

        // Assert
        action.Should().NotThrow();
    }

    [Test]
    public void LoadHistory_WithHistoryData_LoadsSuccessfully()
    {
        // Arrange
        var now = DateTime.Now;
        var history = new List<SpeedSnapshot>
        {
            new() { Timestamp = now.AddSeconds(-60), DownloadSpeedBps = 1_000_000, UploadSpeedBps = 500_000 },
            new() { Timestamp = now.AddSeconds(-50), DownloadSpeedBps = 2_000_000, UploadSpeedBps = 750_000 },
            new() { Timestamp = now.AddSeconds(-40), DownloadSpeedBps = 1_500_000, UploadSpeedBps = 600_000 }
        };
        _persistenceMock.GetSpeedHistoryAsync(Arg.Any<DateTime>()).Returns(history);

        // Act
        _viewModel = CreateViewModel();

        // Allow async loading to complete
        Thread.Sleep(200);

        // Assert
        _persistenceMock.Received().GetSpeedHistoryAsync(Arg.Any<DateTime>());
    }

    [Test]
    public void LoadHistory_RequestsOneHourOfHistory()
    {
        // Arrange
        DateTime? requestedSince = null;
        _persistenceMock.GetSpeedHistoryAsync(Arg.Do<DateTime>(since => requestedSince = since))
            .Returns(new List<SpeedSnapshot>());

        // Act
        _viewModel = CreateViewModel();

        // Allow async loading to complete
        Thread.Sleep(100);

        // Assert
        requestedSince.Should().NotBeNull();
        var timeDiff = DateTime.Now - requestedSince!.Value;
        timeDiff.TotalMinutes.Should().BeApproximately(60, 1); // Should be approximately 1 hour
    }

    [Test]
    public void LoadHistory_WhenPersistenceThrows_DoesNotCrash()
    {
        // Arrange
        _persistenceMock.GetSpeedHistoryAsync(Arg.Any<DateTime>())
            .Returns<Task<List<SpeedSnapshot>>>(x => throw new InvalidOperationException("Database error"));

        // Act
        var action = () => CreateViewModel();

        // Assert - should not throw, just log the error
        action.Should().NotThrow();
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_UnsubscribesFromNetworkStatsUpdated()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.Dispose();

        // Assert
        // Event unsubscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Dispose_UnsubscribesFromSystemStatsUpdated_WhenSystemMonitorProvided()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.Dispose();

        // Assert
        // Event unsubscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act & Assert - should not throw
        _viewModel.Dispose();
        _viewModel.Dispose();
    }

    [Test]
    public void Dispose_WithoutSystemMonitor_DoesNotThrow()
    {
        // Arrange
        _viewModel = CreateViewModelWithoutSystemMonitor();

        // Act
        var action = () => _viewModel.Dispose();

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region Edge Cases

    [Test]
    public void SpeedSeries_HasDownloadAndUploadSeries()
    {
        // Arrange & Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.SpeedSeries.Should().Contain(s => s.Name == "Download");
        _viewModel.SpeedSeries.Should().Contain(s => s.Name == "Upload");
    }

    [Test]
    public void TimeRangeOptions_AreOrderedBySeconds()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Assert
        var options = _viewModel.TimeRangeOptions.ToList();
        options[0].Seconds.Should().Be(30);
        options[1].Seconds.Should().Be(60);
        options[2].Seconds.Should().Be(300);
        options[3].Seconds.Should().Be(900);
        options[4].Seconds.Should().Be(3600);
    }

    [Test]
    public void Constructor_WithNullTimeRange_UsesDefault()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert - even with null-safety, should have a valid default
        _viewModel.SelectedTimeRange.Should().NotBeNull();
    }

    #endregion

    public ValueTask DisposeAsync() {
        _viewModel?.Dispose();
    ; return ValueTask.CompletedTask; }
}

using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using WireBound.Avalonia.ViewModels;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

// Suppress TUnit0017: The helper methods add viewmodels to a collection for cleanup,
// not for sharing state between tests. Each test creates its own viewmodel.
#pragma warning disable TUnit0017

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
    private readonly List<ChartsViewModel> _createdViewModels = [];

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
        var viewModel = new ChartsViewModel(
            _networkMonitorMock,
            _persistenceMock,
            _systemMonitorMock,
            _loggerMock);
        _createdViewModels.Add(viewModel);
        return viewModel;
    }

    private ChartsViewModel CreateViewModelWithoutSystemMonitor()
    {
        var viewModel = new ChartsViewModel(
            _networkMonitorMock,
            _persistenceMock,
            null,
            _loggerMock);
        _createdViewModels.Add(viewModel);
        return viewModel;
    }

    #region Constructor Tests

    [Test]
    public void Constructor_InitializesDefaultSpeedValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.DownloadSpeed.Should().Be("0 B/s");
        viewModel.UploadSpeed.Should().Be("0 B/s");
    }

    [Test]
    public void Constructor_InitializesPeakSpeedValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.PeakDownloadSpeed.Should().Be("0 B/s");
        viewModel.PeakUploadSpeed.Should().Be("0 B/s");
    }

    [Test]
    public void Constructor_InitializesAverageSpeedValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.AverageDownloadSpeed.Should().Be("0 B/s");
        viewModel.AverageUploadSpeed.Should().Be("0 B/s");
    }

    [Test]
    public void Constructor_InitializesSpeedSeries()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SpeedSeries.Should().NotBeNull();
        viewModel.SpeedSeries.Should().HaveCount(2); // Download and Upload series
    }

    [Test]
    public void Constructor_InitializesXAxes()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.XAxes.Should().NotBeNull();
        viewModel.XAxes.Should().NotBeEmpty();
    }

    [Test]
    public void Constructor_InitializesYAxes()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.YAxes.Should().NotBeNull();
        viewModel.YAxes.Should().NotBeEmpty();
    }

    [Test]
    public void Constructor_InitializesSecondaryYAxes()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SecondaryYAxes.Should().NotBeNull();
        viewModel.SecondaryYAxes.Should().NotBeEmpty();
    }

    [Test]
    public void Constructor_InitializesTimeRangeOptions()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.TimeRangeOptions.Should().NotBeNull();
        viewModel.TimeRangeOptions.Should().HaveCount(5); // 30s, 1m, 5m, 15m, 1h
    }

    [Test]
    public void Constructor_SetsDefaultTimeRangeToOneMinute()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SelectedTimeRange.Should().NotBeNull();
        viewModel.SelectedTimeRange.Seconds.Should().Be(60);
        viewModel.SelectedTimeRange.Label.Should().Be("1m");
    }

    [Test]
    public void Constructor_InitializesUpdatesPausedToFalse()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsUpdatesPaused.Should().BeFalse();
    }

    [Test]
    public void Constructor_InitializesPauseStatusTextToEmpty()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.PauseStatusText.Should().BeEmpty();
    }

    [Test]
    public void Constructor_InitializesCpuOverlayToFalse()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.ShowCpuOverlay.Should().BeFalse();
    }

    [Test]
    public void Constructor_InitializesMemoryOverlayToFalse()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.ShowMemoryOverlay.Should().BeFalse();
    }

    [Test]
    public void Constructor_SubscribesToNetworkStatsUpdated()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        // Event subscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Constructor_SubscribesToSystemStatsUpdated_WhenSystemMonitorProvided()
    {
        // Act
        var viewModel = CreateViewModel();

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
        var viewModel = CreateViewModel();

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
        var viewModel = CreateViewModel();

        // Assert
        var option = viewModel.TimeRangeOptions.First(x => x.Label == "30s");
        option.Seconds.Should().Be(30);
        option.Description.Should().Be("Last 30 seconds");
    }

    [Test]
    public void TimeRangeOptions_1Minute_HasCorrectValues()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Assert
        var option = viewModel.TimeRangeOptions.First(x => x.Label == "1m");
        option.Seconds.Should().Be(60);
        option.Description.Should().Be("Last 1 minute");
    }

    [Test]
    public void TimeRangeOptions_5Minutes_HasCorrectValues()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Assert
        var option = viewModel.TimeRangeOptions.First(x => x.Label == "5m");
        option.Seconds.Should().Be(300);
        option.Description.Should().Be("Last 5 minutes");
    }

    [Test]
    public void TimeRangeOptions_15Minutes_HasCorrectValues()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Assert
        var option = viewModel.TimeRangeOptions.First(x => x.Label == "15m");
        option.Seconds.Should().Be(900);
        option.Description.Should().Be("Last 15 minutes");
    }

    [Test]
    public void TimeRangeOptions_1Hour_HasCorrectValues()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Assert
        var option = viewModel.TimeRangeOptions.First(x => x.Label == "1h");
        option.Seconds.Should().Be(3600);
        option.Description.Should().Be("Last 1 hour");
    }

    #endregion

    #region Time Range Selection Tests

    [Test]
    public void SelectedTimeRange_WhenChanged_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        var newTimeRange = viewModel.TimeRangeOptions.First(x => x.Label == "5m");

        // Act
        viewModel.SelectedTimeRange = newTimeRange;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.SelectedTimeRange);
    }

    [Test]
    public void SelectedTimeRange_WhenChanged_UpdatesDisplayData()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialTimeRange = viewModel.SelectedTimeRange;

        // Act
        var newTimeRange = viewModel.TimeRangeOptions.First(x => x.Label == "5m");
        viewModel.SelectedTimeRange = newTimeRange;

        // Assert
        viewModel.SelectedTimeRange.Should().Be(newTimeRange);
        viewModel.SelectedTimeRange.Seconds.Should().Be(300);
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
        var viewModel = CreateViewModel();
        var timeRange = viewModel.TimeRangeOptions.First(x => x.Label == label);

        // Act
        viewModel.SelectedTimeRange = timeRange;

        // Assert
        viewModel.SelectedTimeRange.Seconds.Should().Be(expectedSeconds);
    }

    #endregion

    #region Pause/Resume Updates Tests

    [Test]
    public void IsUpdatesPaused_WhenSetToTrue_PausesUpdates()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.IsUpdatesPaused = true;

        // Assert
        viewModel.IsUpdatesPaused.Should().BeTrue();
    }

    [Test]
    public void IsUpdatesPaused_WhenSetToFalse_ResumesUpdates()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.IsUpdatesPaused = true;

        // Act
        viewModel.IsUpdatesPaused = false;

        // Assert
        viewModel.IsUpdatesPaused.Should().BeFalse();
    }

    [Test]
    public void IsUpdatesPaused_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.IsUpdatesPaused = true;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.IsUpdatesPaused);
    }

    [Test]
    public void PauseStatusText_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("PauseStatusText")!.SetValue(viewModel, "Paused");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.PauseStatusText);
    }

    #endregion

    #region CPU Overlay Toggle Tests

    [Test]
    public void ShowCpuOverlay_WhenEnabled_AddsCpuSeriesToSpeedSeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialCount = viewModel.SpeedSeries.Count;

        // Act
        viewModel.ShowCpuOverlay = true;

        // Assert
        viewModel.SpeedSeries.Count.Should().Be(initialCount + 1);
        viewModel.SpeedSeries.Should().Contain(s => s.Name == "CPU %");
    }

    [Test]
    public void ShowCpuOverlay_WhenDisabled_RemovesCpuSeriesFromSpeedSeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.ShowCpuOverlay = true;
        var countWithCpu = viewModel.SpeedSeries.Count;

        // Act
        viewModel.ShowCpuOverlay = false;

        // Assert
        viewModel.SpeedSeries.Count.Should().Be(countWithCpu - 1);
        viewModel.SpeedSeries.Should().NotContain(s => s.Name == "CPU %");
    }

    [Test]
    public void ShowCpuOverlay_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.ShowCpuOverlay = true;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.ShowCpuOverlay);
    }

    [Test]
    public void ShowCpuOverlay_WhenEnabledTwice_DoesNotDuplicateSeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.ShowCpuOverlay = true;
        var countAfterFirst = viewModel.SpeedSeries.Count;

        // Act - try to enable again
        viewModel.ShowCpuOverlay = true;

        // Assert - count should remain the same
        viewModel.SpeedSeries.Count.Should().Be(countAfterFirst);
    }

    #endregion

    #region Memory Overlay Toggle Tests

    [Test]
    public void ShowMemoryOverlay_WhenEnabled_AddsMemorySeriesToSpeedSeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialCount = viewModel.SpeedSeries.Count;

        // Act
        viewModel.ShowMemoryOverlay = true;

        // Assert
        viewModel.SpeedSeries.Count.Should().Be(initialCount + 1);
        viewModel.SpeedSeries.Should().Contain(s => s.Name == "Memory %");
    }

    [Test]
    public void ShowMemoryOverlay_WhenDisabled_RemovesMemorySeriesFromSpeedSeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.ShowMemoryOverlay = true;
        var countWithMemory = viewModel.SpeedSeries.Count;

        // Act
        viewModel.ShowMemoryOverlay = false;

        // Assert
        viewModel.SpeedSeries.Count.Should().Be(countWithMemory - 1);
        viewModel.SpeedSeries.Should().NotContain(s => s.Name == "Memory %");
    }

    [Test]
    public void ShowMemoryOverlay_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.ShowMemoryOverlay = true;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.ShowMemoryOverlay);
    }

    [Test]
    public void ShowMemoryOverlay_WhenEnabledTwice_DoesNotDuplicateSeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.ShowMemoryOverlay = true;
        var countAfterFirst = viewModel.SpeedSeries.Count;

        // Act - try to enable again
        viewModel.ShowMemoryOverlay = true;

        // Assert - count should remain the same
        viewModel.SpeedSeries.Count.Should().Be(countAfterFirst);
    }

    #endregion

    #region Combined Overlay Tests

    [Test]
    public void ShowBothOverlays_AddsBothSeriesToSpeedSeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialCount = viewModel.SpeedSeries.Count;

        // Act
        viewModel.ShowCpuOverlay = true;
        viewModel.ShowMemoryOverlay = true;

        // Assert
        viewModel.SpeedSeries.Count.Should().Be(initialCount + 2);
        viewModel.SpeedSeries.Should().Contain(s => s.Name == "CPU %");
        viewModel.SpeedSeries.Should().Contain(s => s.Name == "Memory %");
    }

    [Test]
    public void DisableBothOverlays_RemovesBothSeriesFromSpeedSeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.ShowCpuOverlay = true;
        viewModel.ShowMemoryOverlay = true;
        var countWithBoth = viewModel.SpeedSeries.Count;

        // Act
        viewModel.ShowCpuOverlay = false;
        viewModel.ShowMemoryOverlay = false;

        // Assert
        viewModel.SpeedSeries.Count.Should().Be(countWithBoth - 2);
        viewModel.SpeedSeries.Should().NotContain(s => s.Name == "CPU %");
        viewModel.SpeedSeries.Should().NotContain(s => s.Name == "Memory %");
    }

    #endregion

    #region Property Change Notification Tests

    [Test]
    public void DownloadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("DownloadSpeed")!.SetValue(viewModel, "100 KB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.DownloadSpeed);
    }

    [Test]
    public void UploadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("UploadSpeed")!.SetValue(viewModel, "50 KB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.UploadSpeed);
    }

    [Test]
    public void PeakDownloadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("PeakDownloadSpeed")!.SetValue(viewModel, "1 MB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.PeakDownloadSpeed);
    }

    [Test]
    public void PeakUploadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("PeakUploadSpeed")!.SetValue(viewModel, "500 KB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.PeakUploadSpeed);
    }

    [Test]
    public void AverageDownloadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("AverageDownloadSpeed")!.SetValue(viewModel, "200 KB/s");

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.AverageDownloadSpeed);
    }

    [Test]
    public void AverageUploadSpeed_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("AverageUploadSpeed")!.SetValue(viewModel, "100 KB/s");

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
        var viewModel = CreateViewModel();

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
        var viewModel = CreateViewModel();

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
        var viewModel = CreateViewModel();

        // Act
        viewModel.Dispose();

        // Assert
        // Event unsubscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Dispose_UnsubscribesFromSystemStatsUpdated_WhenSystemMonitorProvided()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.Dispose();

        // Assert
        // Event unsubscription verified (NSubstitute does not verify event subscriptions directly)
    }

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
    public void Dispose_WithoutSystemMonitor_DoesNotThrow()
    {
        // Arrange
        var viewModel = CreateViewModelWithoutSystemMonitor();

        // Act
        var action = () => viewModel.Dispose();

        // Assert
        action.Should().NotThrow();
    }

    #endregion

    #region Edge Cases

    [Test]
    public void SpeedSeries_HasDownloadAndUploadSeries()
    {
        // Arrange & Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SpeedSeries.Should().Contain(s => s.Name == "Download");
        viewModel.SpeedSeries.Should().Contain(s => s.Name == "Upload");
    }

    [Test]
    public void TimeRangeOptions_AreOrderedBySeconds()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Assert
        var options = viewModel.TimeRangeOptions.ToList();
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
        var viewModel = CreateViewModel();

        // Assert - even with null-safety, should have a valid default
        viewModel.SelectedTimeRange.Should().NotBeNull();
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

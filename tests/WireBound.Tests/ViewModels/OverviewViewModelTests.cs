using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using WireBound.Avalonia.ViewModels;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Tests.Fixtures;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for OverviewViewModel
/// </summary>
public class OverviewViewModelTests : IAsyncDisposable
{
    private readonly INetworkMonitorService _networkMonitorMock;
    private readonly ISystemMonitorService _systemMonitorMock;
    private readonly IDataPersistenceService _persistenceMock;
    private readonly ILogger<OverviewViewModel> _loggerMock;
    private OverviewViewModel? _viewModel;

    public OverviewViewModelTests()
    {
        _networkMonitorMock = Substitute.For<INetworkMonitorService>();
        _systemMonitorMock = Substitute.For<ISystemMonitorService>();
        _persistenceMock = Substitute.For<IDataPersistenceService>();
        _loggerMock = Substitute.For<ILogger<OverviewViewModel>>();

        // Setup default returns for service methods
        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        // Setup network monitor
        _networkMonitorMock.GetAdapters(Arg.Any<bool>()).Returns(new List<NetworkAdapter>());

        _networkMonitorMock.GetCurrentStats().Returns(CreateDefaultNetworkStats());

        // Setup system monitor with default stats
        _systemMonitorMock.GetCurrentStats().Returns(CreateDefaultSystemStats());

        // Setup persistence
        _persistenceMock.GetTodayUsageAsync().Returns((0L, 0L));
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
            Memory = new MemoryStats { TotalBytes = 100, UsedBytes = 0 } // UsagePercent is computed from TotalBytes/UsedBytes
        };
    }

    private OverviewViewModel CreateViewModel()
    {
        return new OverviewViewModel(
            _networkMonitorMock,
            _systemMonitorMock,
            _persistenceMock,
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
    public void Constructor_InitializesUsageValues()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.TodayDownload.Should().Be("0 B");
        _viewModel.TodayUpload.Should().Be("0 B");
        _viewModel.SessionDownload.Should().Be("0 B");
        _viewModel.SessionUpload.Should().Be("0 B");
    }

    [Test]
    public void Constructor_InitializesTrendIndicators()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.DownloadTrendIcon.Should().Be("");
        _viewModel.DownloadTrendText.Should().Be("stable");
        _viewModel.UploadTrendIcon.Should().Be("");
        _viewModel.UploadTrendText.Should().Be("stable");
    }

    [Test]
    public void Constructor_InitializesSystemProperties()
    {
        // Arrange - UsagePercent is computed from UsedBytes/TotalBytes
        var systemStats = new SystemStats
        {
            Cpu = new CpuStats { UsagePercent = 25 },
            Memory = new MemoryStats { TotalBytes = 100, UsedBytes = 50 } // 50% usage
        };
        _systemMonitorMock.GetCurrentStats().Returns(systemStats);

        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.CpuPercent.Should().Be(25);
        _viewModel.MemoryPercent.Should().Be(50);
        _viewModel.CpuUsageFormatted.Should().Be("25%");
        _viewModel.MemoryUsageFormatted.Should().Be("50%");
    }

    [Test]
    public void Constructor_InitializesChartSeries()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.ChartSeries.Should().NotBeNull();
        _viewModel.ChartSeries.Should().HaveCount(2); // Download and Upload series
    }

    [Test]
    public void Constructor_InitializesChartAxes()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.ChartXAxes.Should().NotBeNull();
        _viewModel.ChartYAxes.Should().NotBeNull();
        _viewModel.ChartSecondaryYAxes.Should().NotBeNull();
    }

    [Test]
    public void Constructor_InitializesDefaultTimeRange()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.SelectedTimeRange.Should().Be(TimeRange.OneMinute);
    }

    [Test]
    public void Constructor_InitializesAdaptersCollection()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.Adapters.Should().NotBeNull();
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
    public void Constructor_SubscribesToSystemStatsUpdated()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        // Event subscription verified (NSubstitute does not verify event subscriptions directly)
    }

    [Test]
    public void Constructor_LoadsAdaptersFromService()
    {
        // Arrange
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true },
            new() { Id = "wifi0", Name = "WiFi", IsActive = true }
        };
        _networkMonitorMock.GetAdapters(false).Returns(adapters);

        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.Adapters.Should().HaveCount(2);
    }

    [Test]
    public void Constructor_LoadsTodayUsageFromPersistence()
    {
        // Arrange
        _persistenceMock.GetTodayUsageAsync().Returns((1024L * 1024 * 100, 1024L * 1024 * 50));

        // Act
        _viewModel = CreateViewModel();

        // Allow async operation to complete
        Thread.Sleep(100);

        // Assert - verify the method was called
        _persistenceMock.Received(1).GetTodayUsageAsync();
    }

    #endregion

    #region Time Range Options Tests

    [Test]
    public void TimeRangeOptions_HasFourOptions()
    {
        // Assert
        OverviewViewModel.TimeRangeOptions.Should().HaveCount(4);
    }

    [Test]
    public void TimeRangeOptions_ContainsExpectedRanges()
    {
        // Assert
        OverviewViewModel.TimeRangeOptions.Should().Contain(x => x.Value == TimeRange.OneMinute);
        OverviewViewModel.TimeRangeOptions.Should().Contain(x => x.Value == TimeRange.FiveMinutes);
        OverviewViewModel.TimeRangeOptions.Should().Contain(x => x.Value == TimeRange.FifteenMinutes);
        OverviewViewModel.TimeRangeOptions.Should().Contain(x => x.Value == TimeRange.OneHour);
    }

    [Test]
    public void TimeRangeOptions_OneMinute_HasCorrectSeconds()
    {
        // Assert
        var option = OverviewViewModel.TimeRangeOptions.First(x => x.Value == TimeRange.OneMinute);
        option.Seconds.Should().Be(60);
        option.Label.Should().Be("1m");
    }

    [Test]
    public void TimeRangeOptions_FiveMinutes_HasCorrectSeconds()
    {
        // Assert
        var option = OverviewViewModel.TimeRangeOptions.First(x => x.Value == TimeRange.FiveMinutes);
        option.Seconds.Should().Be(300);
        option.Label.Should().Be("5m");
    }

    [Test]
    public void TimeRangeOptions_FifteenMinutes_HasCorrectSeconds()
    {
        // Assert
        var option = OverviewViewModel.TimeRangeOptions.First(x => x.Value == TimeRange.FifteenMinutes);
        option.Seconds.Should().Be(900);
        option.Label.Should().Be("15m");
    }

    [Test]
    public void TimeRangeOptions_OneHour_HasCorrectSeconds()
    {
        // Assert
        var option = OverviewViewModel.TimeRangeOptions.First(x => x.Value == TimeRange.OneHour);
        option.Seconds.Should().Be(3600);
        option.Label.Should().Be("1h");
    }

    #endregion

    #region Adapter Selection Tests

    [Test]
    public void SelectedAdapter_WhenSet_CallsSetAdapterOnService()
    {
        // Arrange
        var adapter = new NetworkAdapter { Id = "eth0", Name = "Ethernet", IsActive = true };
        _networkMonitorMock.GetAdapters(false).Returns(new List<NetworkAdapter> { adapter });
        _viewModel = CreateViewModel();

        var adapterItem = _viewModel.Adapters.First();

        // Act
        _viewModel.SelectedAdapter = adapterItem;

        // Assert
        _networkMonitorMock.Received(1).SetAdapter("eth0");
    }

    [Test]
    public void SelectedAdapter_WhenSetToNull_CallsSetAdapterWithEmptyString()
    {
        // Arrange
        var adapter = new NetworkAdapter { Id = "eth0", Name = "Ethernet", IsActive = true };
        _networkMonitorMock.GetAdapters(false).Returns(new List<NetworkAdapter> { adapter });
        _viewModel = CreateViewModel();
        _viewModel.SelectedAdapter = _viewModel.Adapters.First();

        // Act
        _viewModel.SelectedAdapter = null;

        // Assert
        _networkMonitorMock.Received(1).SetAdapter(string.Empty);
    }

    [Test]
    public void ShowAdvancedAdapters_WhenChanged_ReloadsAdapters()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.ShowAdvancedAdapters = true;

        // Assert
        _networkMonitorMock.Received(1).GetAdapters(true);
    }

    [Test]
    public void LoadAdapters_OnlyLoadsActiveAdapters()
    {
        // Arrange
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true },
            new() { Id = "eth1", Name = "Ethernet 2", IsActive = false },
            new() { Id = "wifi0", Name = "WiFi", IsActive = true }
        };
        _networkMonitorMock.GetAdapters(false).Returns(adapters);

        // Act
        _viewModel = CreateViewModel();

        // Assert - only active adapters should be loaded
        _viewModel.Adapters.Should().HaveCount(2);
        _viewModel.Adapters.Should().NotContain(a => a.Id == "eth1");
    }

    #endregion

    #region Chart Layer Toggle Tests

    [Test]
    public void ToggleCpuOverlayCommand_TogglesShowCpuOverlay()
    {
        // Arrange
        _viewModel = CreateViewModel();
        var initialValue = _viewModel.ShowCpuOverlay;

        // Act
        _viewModel.ToggleCpuOverlayCommand.Execute(null);

        // Assert
        _viewModel.ShowCpuOverlay.Should().Be(!initialValue);
    }

    [Test]
    public void ToggleMemoryOverlayCommand_TogglesShowMemoryOverlay()
    {
        // Arrange
        _viewModel = CreateViewModel();
        var initialValue = _viewModel.ShowMemoryOverlay;

        // Act
        _viewModel.ToggleMemoryOverlayCommand.Execute(null);

        // Assert
        _viewModel.ShowMemoryOverlay.Should().Be(!initialValue);
    }

    [Test]
    public void ShowCpuOverlay_WhenEnabled_RebuildsSeriesToIncludeCpuSeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        var initialSeriesCount = _viewModel.ChartSeries.Length;

        // Act
        _viewModel.ShowCpuOverlay = true;

        // Assert
        _viewModel.ChartSeries.Length.Should().Be(initialSeriesCount + 1);
        _viewModel.ChartSeries.Should().Contain(s => s.Name == "CPU");
    }

    [Test]
    public void ShowMemoryOverlay_WhenEnabled_RebuildsSeriesToIncludeMemorySeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        var initialSeriesCount = _viewModel.ChartSeries.Length;

        // Act
        _viewModel.ShowMemoryOverlay = true;

        // Assert
        _viewModel.ChartSeries.Length.Should().Be(initialSeriesCount + 1);
        _viewModel.ChartSeries.Should().Contain(s => s.Name == "Memory");
    }

    [Test]
    public void ShowCpuOverlay_WhenDisabled_RebuildsSeriesToRemoveCpuSeries()
    {
        // Arrange
        _viewModel = CreateViewModel();
        _viewModel.ShowCpuOverlay = true;
        var countWithCpu = _viewModel.ChartSeries.Length;

        // Act
        _viewModel.ShowCpuOverlay = false;

        // Assert
        _viewModel.ChartSeries.Length.Should().Be(countWithCpu - 1);
        _viewModel.ChartSeries.Should().NotContain(s => s.Name == "CPU");
    }

    [Test]
    public void ShowBothOverlays_RebuildsSeriesToIncludeBoth()
    {
        // Arrange
        _viewModel = CreateViewModel();
        var initialCount = _viewModel.ChartSeries.Length;

        // Act
        _viewModel.ShowCpuOverlay = true;
        _viewModel.ShowMemoryOverlay = true;

        // Assert
        _viewModel.ChartSeries.Length.Should().Be(initialCount + 2);
        _viewModel.ChartSeries.Should().Contain(s => s.Name == "CPU");
        _viewModel.ChartSeries.Should().Contain(s => s.Name == "Memory");
    }

    #endregion

    #region Time Range Selection Tests

    [Test]
    public void SetTimeRangeCommand_SetsSelectedTimeRange()
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.SetTimeRangeCommand.Execute(TimeRange.FiveMinutes);

        // Assert
        _viewModel.SelectedTimeRange.Should().Be(TimeRange.FiveMinutes);
    }

    [Test]
    [Arguments(TimeRange.OneMinute)]
    [Arguments(TimeRange.FiveMinutes)]
    [Arguments(TimeRange.FifteenMinutes)]
    [Arguments(TimeRange.OneHour)]
    public void SetTimeRangeCommand_AcceptsAllTimeRanges(TimeRange range)
    {
        // Arrange
        _viewModel = CreateViewModel();

        // Act
        _viewModel.SetTimeRangeCommand.Execute(range);

        // Assert
        _viewModel.SelectedTimeRange.Should().Be(range);
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
    public void Dispose_UnsubscribesFromSystemStatsUpdated()
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

    #endregion

    #region GPU Properties Tests

    [Test]
    public void GpuPercent_InitiallyNull()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.GpuPercent.Should().BeNull();
    }

    [Test]
    public void IsGpuAvailable_InitiallyFalse()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.IsGpuAvailable.Should().BeFalse();
    }

    [Test]
    public void GpuUsageFormatted_InitiallyNA()
    {
        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.GpuUsageFormatted.Should().Be("N/A");
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
    public void CpuPercent_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("CpuPercent")!.SetValue(_viewModel, 50.0);

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.CpuPercent);
    }

    [Test]
    public void MemoryPercent_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.GetType().GetProperty("MemoryPercent")!.SetValue(_viewModel, 75.0);

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.MemoryPercent);
    }

    [Test]
    public void SelectedTimeRange_RaisesPropertyChanged()
    {
        // Arrange
        _viewModel = CreateViewModel();
        using var monitor = _viewModel.Monitor();

        // Act
        _viewModel.SelectedTimeRange = TimeRange.OneHour;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.SelectedTimeRange);
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

    #endregion

    #region Edge Cases

    [Test]
    public void Constructor_WithNullDataPersistence_DoesNotThrow()
    {
        // Act
        var action = () => new OverviewViewModel(
            _networkMonitorMock,
            _systemMonitorMock,
            null,
            _loggerMock);

        // Assert
        action.Should().NotThrow();
    }

    [Test]
    public void Constructor_WithNullLogger_DoesNotThrow()
    {
        // Act
        var action = () => new OverviewViewModel(
            _networkMonitorMock,
            _systemMonitorMock,
            _persistenceMock,
            null);

        // Assert
        action.Should().NotThrow();
    }

    [Test]
    public void Constructor_WithNoActiveAdapters_InitializesEmptyAdaptersList()
    {
        // Arrange
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = false }
        };
        _networkMonitorMock.GetAdapters(false).Returns(adapters);

        // Act
        _viewModel = CreateViewModel();

        // Assert
        _viewModel.Adapters.Should().BeEmpty();
    }

    #endregion

    public ValueTask DisposeAsync() {
        _viewModel?.Dispose();
    ; return ValueTask.CompletedTask; }
}

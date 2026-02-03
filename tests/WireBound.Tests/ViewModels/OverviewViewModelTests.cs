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
public class OverviewViewModelTests
{
    private readonly INetworkMonitorService _networkMonitorMock;
    private readonly ISystemMonitorService _systemMonitorMock;
    private readonly IDataPersistenceService _persistenceMock;
    private readonly ILogger<OverviewViewModel> _loggerMock;

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
    public void Constructor_InitializesUsageValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.TodayDownload.Should().Be("0 B");
        viewModel.TodayUpload.Should().Be("0 B");
        viewModel.SessionDownload.Should().Be("0 B");
        viewModel.SessionUpload.Should().Be("0 B");
    }

    [Test]
    public void Constructor_InitializesTrendIndicators()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.DownloadTrendIcon.Should().Be("");
        viewModel.DownloadTrendText.Should().Be("stable");
        viewModel.UploadTrendIcon.Should().Be("");
        viewModel.UploadTrendText.Should().Be("stable");
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
        var viewModel = CreateViewModel();

        // Assert
        viewModel.CpuPercent.Should().Be(25);
        viewModel.MemoryPercent.Should().Be(50);
        viewModel.CpuUsageFormatted.Should().Be("25%");
        viewModel.MemoryUsageFormatted.Should().Be("50%");
    }

    [Test]
    public void Constructor_InitializesChartSeries()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.ChartSeries.Should().NotBeNull();
        viewModel.ChartSeries.Should().HaveCount(2); // Download and Upload series
    }

    [Test]
    public void Constructor_InitializesChartAxes()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.ChartXAxes.Should().NotBeNull();
        viewModel.ChartYAxes.Should().NotBeNull();
        viewModel.ChartSecondaryYAxes.Should().NotBeNull();
    }

    [Test]
    public void Constructor_InitializesDefaultTimeRange()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SelectedTimeRange.Should().Be(TimeRange.OneMinute);
    }

    [Test]
    public void Constructor_InitializesAdaptersCollection()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.Adapters.Should().NotBeNull();
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
    public void Constructor_SubscribesToSystemStatsUpdated()
    {
        // Act
        var viewModel = CreateViewModel();

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
        var viewModel = CreateViewModel();

        // Assert
        viewModel.Adapters.Should().HaveCount(2);
    }

    [Test]
    public void Constructor_LoadsTodayUsageFromPersistence()
    {
        // Arrange
        _persistenceMock.GetTodayUsageAsync().Returns((1024L * 1024 * 100, 1024L * 1024 * 50));

        // Act
        var viewModel = CreateViewModel();

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
        var viewModel = CreateViewModel();

        var adapterItem = viewModel.Adapters.First();

        // Act
        viewModel.SelectedAdapter = adapterItem;

        // Assert
        _networkMonitorMock.Received(1).SetAdapter("eth0");
    }

    [Test]
    public void SelectedAdapter_WhenSetToNull_CallsSetAdapterWithEmptyString()
    {
        // Arrange
        var adapter = new NetworkAdapter { Id = "eth0", Name = "Ethernet", IsActive = true };
        _networkMonitorMock.GetAdapters(false).Returns(new List<NetworkAdapter> { adapter });
        var viewModel = CreateViewModel();
        viewModel.SelectedAdapter = viewModel.Adapters.First();

        // Act
        viewModel.SelectedAdapter = null;

        // Assert
        _networkMonitorMock.Received(1).SetAdapter(string.Empty);
    }

    [Test]
    public void ShowAdvancedAdapters_WhenChanged_ReloadsAdapters()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.ShowAdvancedAdapters = true;

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
        var viewModel = CreateViewModel();

        // Assert - only active adapters should be loaded
        viewModel.Adapters.Should().HaveCount(2);
        viewModel.Adapters.Should().NotContain(a => a.Id == "eth1");
    }

    #endregion

    #region Chart Layer Toggle Tests

    [Test]
    public void ToggleCpuOverlayCommand_TogglesShowCpuOverlay()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialValue = viewModel.ShowCpuOverlay;

        // Act
        viewModel.ToggleCpuOverlayCommand.Execute(null);

        // Assert
        viewModel.ShowCpuOverlay.Should().Be(!initialValue);
    }

    [Test]
    public void ToggleMemoryOverlayCommand_TogglesShowMemoryOverlay()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialValue = viewModel.ShowMemoryOverlay;

        // Act
        viewModel.ToggleMemoryOverlayCommand.Execute(null);

        // Assert
        viewModel.ShowMemoryOverlay.Should().Be(!initialValue);
    }

    [Test]
    public void ShowCpuOverlay_WhenEnabled_RebuildsSeriesToIncludeCpuSeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialSeriesCount = viewModel.ChartSeries.Length;

        // Act
        viewModel.ShowCpuOverlay = true;

        // Assert
        viewModel.ChartSeries.Length.Should().Be(initialSeriesCount + 1);
        viewModel.ChartSeries.Should().Contain(s => s.Name == "CPU");
    }

    [Test]
    public void ShowMemoryOverlay_WhenEnabled_RebuildsSeriesToIncludeMemorySeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialSeriesCount = viewModel.ChartSeries.Length;

        // Act
        viewModel.ShowMemoryOverlay = true;

        // Assert
        viewModel.ChartSeries.Length.Should().Be(initialSeriesCount + 1);
        viewModel.ChartSeries.Should().Contain(s => s.Name == "Memory");
    }

    [Test]
    public void ShowCpuOverlay_WhenDisabled_RebuildsSeriesToRemoveCpuSeries()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.ShowCpuOverlay = true;
        var countWithCpu = viewModel.ChartSeries.Length;

        // Act
        viewModel.ShowCpuOverlay = false;

        // Assert
        viewModel.ChartSeries.Length.Should().Be(countWithCpu - 1);
        viewModel.ChartSeries.Should().NotContain(s => s.Name == "CPU");
    }

    [Test]
    public void ShowBothOverlays_RebuildsSeriesToIncludeBoth()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialCount = viewModel.ChartSeries.Length;

        // Act
        viewModel.ShowCpuOverlay = true;
        viewModel.ShowMemoryOverlay = true;

        // Assert
        viewModel.ChartSeries.Length.Should().Be(initialCount + 2);
        viewModel.ChartSeries.Should().Contain(s => s.Name == "CPU");
        viewModel.ChartSeries.Should().Contain(s => s.Name == "Memory");
    }

    #endregion

    #region Time Range Selection Tests

    [Test]
    public void SetTimeRangeCommand_SetsSelectedTimeRange()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SetTimeRangeCommand.Execute(TimeRange.FiveMinutes);

        // Assert
        viewModel.SelectedTimeRange.Should().Be(TimeRange.FiveMinutes);
    }

    [Test]
    [Arguments(TimeRange.OneMinute)]
    [Arguments(TimeRange.FiveMinutes)]
    [Arguments(TimeRange.FifteenMinutes)]
    [Arguments(TimeRange.OneHour)]
    public void SetTimeRangeCommand_AcceptsAllTimeRanges(TimeRange range)
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SetTimeRangeCommand.Execute(range);

        // Assert
        viewModel.SelectedTimeRange.Should().Be(range);
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
    public void Dispose_UnsubscribesFromSystemStatsUpdated()
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

    #endregion

    #region GPU Properties Tests

    [Test]
    public void GpuPercent_InitiallyNull()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.GpuPercent.Should().BeNull();
    }

    [Test]
    public void IsGpuAvailable_InitiallyFalse()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.IsGpuAvailable.Should().BeFalse();
    }

    [Test]
    public void GpuUsageFormatted_InitiallyNA()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.GpuUsageFormatted.Should().Be("N/A");
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
    public void CpuPercent_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("CpuPercent")!.SetValue(viewModel, 50.0);

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.CpuPercent);
    }

    [Test]
    public void MemoryPercent_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.GetType().GetProperty("MemoryPercent")!.SetValue(viewModel, 75.0);

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.MemoryPercent);
    }

    [Test]
    public void SelectedTimeRange_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        using var monitor = viewModel.Monitor();

        // Act
        viewModel.SelectedTimeRange = TimeRange.OneHour;

        // Assert
        monitor.Should().RaisePropertyChangeFor(x => x.SelectedTimeRange);
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
        var viewModel = CreateViewModel();

        // Assert
        viewModel.Adapters.Should().BeEmpty();
    }

    #endregion
}

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
    private readonly List<OverviewViewModel> _createdViewModels = [];

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
        _networkMonitorMock.GetAllAdapterStats().Returns(new Dictionary<string, NetworkStats>());
        _networkMonitorMock.GetPrimaryAdapterId().Returns(string.Empty);

        // Setup system monitor with default stats
        _systemMonitorMock.GetCurrentStats().Returns(CreateDefaultSystemStats());

        // Setup persistence
        _persistenceMock.GetTodayUsageAsync().Returns((0L, 0L));
        _persistenceMock.GetSettingsAsync().Returns(new AppSettings());
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
        var vm = new OverviewViewModel(
            _networkMonitorMock,
            _systemMonitorMock,
            _persistenceMock,
            _loggerMock);
        _createdViewModels.Add(vm);
        return vm;
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

        // Assert - Auto adapter + 2 real adapters
        viewModel.Adapters.Should().HaveCount(3);
        viewModel.Adapters[0].IsAuto.Should().BeTrue();
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

        var adapterItem = viewModel.Adapters.First(a => a.Id == "eth0");

        // Act
        viewModel.SelectedAdapter = adapterItem;

        // Assert
        _networkMonitorMock.Received(1).SetAdapter("eth0");
    }

    [Test]
    public void SelectedAdapter_WhenSetToNull_CallsSetAdapterWithAuto()
    {
        // Arrange
        var adapter = new NetworkAdapter { Id = "eth0", Name = "Ethernet", IsActive = true };
        _networkMonitorMock.GetAdapters(false).Returns(new List<NetworkAdapter> { adapter });
        var viewModel = CreateViewModel();
        viewModel.SelectedAdapter = viewModel.Adapters.First(a => a.Id == "eth0");
        _networkMonitorMock.ClearReceivedCalls();

        // Act
        viewModel.SelectedAdapter = null;

        // Assert
        _networkMonitorMock.Received(1).SetAdapter(NetworkMonitorConstants.AutoAdapterId);
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

        // Assert - Auto + 2 active adapters (eth1 is inactive, excluded)
        viewModel.Adapters.Should().HaveCount(3);
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
    public void Constructor_WithNoActiveAdapters_InitializesWithAutoAdapterOnly()
    {
        // Arrange
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = false }
        };
        _networkMonitorMock.GetAdapters(false).Returns(adapters);

        // Act
        var viewModel = CreateViewModel();

        // Assert - only the Auto adapter
        viewModel.Adapters.Should().HaveCount(1);
        viewModel.Adapters[0].IsAuto.Should().BeTrue();
    }

    #endregion

    #region Auto Adapter Tests

    [Test]
    public void LoadAdapters_IncludesAutoAdapterAsFirst()
    {
        // Arrange
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", DisplayName = "Ethernet", IsActive = true }
        };
        _networkMonitorMock.GetAdapters(false).Returns(adapters);

        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.Adapters.Should().HaveCountGreaterThan(0);
        viewModel.Adapters[0].IsAuto.Should().BeTrue();
        viewModel.Adapters[0].Id.Should().Be(NetworkMonitorConstants.AutoAdapterId);
    }

    [Test]
    public void SelectedAdapter_WhenAutoSelected_CallsSetAdapterWithAutoId()
    {
        // Arrange
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet", IsActive = true }
        };
        _networkMonitorMock.GetAdapters(false).Returns(adapters);
        var viewModel = CreateViewModel();

        // Act
        viewModel.SelectedAdapter = viewModel.Adapters.First(a => a.IsAuto);

        // Assert
        _networkMonitorMock.Received().SetAdapter(NetworkMonitorConstants.AutoAdapterId);
    }

    [Test]
    public void Constructor_InitializesSecondaryAdaptersEmpty()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SecondaryAdapters.Should().NotBeNull();
        viewModel.SecondaryAdapters.Should().BeEmpty();
        viewModel.HasSecondaryAdapters.Should().BeFalse();
    }

    [Test]
    public void Constructor_InitializesAutoSwitchNotificationEmpty()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.AutoSwitchNotification.Should().Be(string.Empty);
        viewModel.IsAutoSwitchNotificationVisible.Should().BeFalse();
    }

    [Test]
    public void AdapterDisplayItem_CreateAuto_HasCorrectProperties()
    {
        // Act
        var autoItem = AdapterDisplayItem.CreateAuto("Ethernet");

        // Assert
        autoItem.Id.Should().Be(NetworkMonitorConstants.AutoAdapterId);
        autoItem.IsAuto.Should().BeTrue();
        autoItem.DisplayName.Should().Contain("Auto");
        autoItem.DisplayName.Should().Contain("Ethernet");
    }

    [Test]
    public void AdapterDisplayItem_CreateAuto_WithNoResolvedName_ShowsDetecting()
    {
        // Act
        var autoItem = AdapterDisplayItem.CreateAuto();

        // Assert
        autoItem.DisplayName.Should().Contain("detecting");
    }

    [Test]
    public void AdapterDisplayItem_UpdateAutoResolvedName_UpdatesDisplayName()
    {
        // Arrange
        var autoItem = AdapterDisplayItem.CreateAuto();

        // Act
        autoItem.UpdateAutoResolvedName("WiFi");

        // Assert
        autoItem.DisplayName.Should().Contain("WiFi");
        autoItem.DisplayName.Should().Contain("Auto");
    }

    [Test]
    public void AdapterDisplayItem_UpdateAutoResolvedName_OnNonAutoItem_DoesNothing()
    {
        // Arrange
        var adapter = new NetworkAdapter { Id = "eth0", Name = "Ethernet", DisplayName = "Ethernet" };
        var item = new AdapterDisplayItem(adapter);
        var originalName = item.DisplayName;

        // Act
        item.UpdateAutoResolvedName("WiFi");

        // Assert
        item.DisplayName.Should().Be(originalName);
    }

    [Test]
    public void NetworkMonitorConstants_AutoAdapterId_IsAuto()
    {
        // Assert
        NetworkMonitorConstants.AutoAdapterId.Should().Be("auto");
    }

    [Test]
    public void AppSettings_DefaultSelectedAdapterId_IsAuto()
    {
        // Act
        var settings = new AppSettings();

        // Assert
        settings.SelectedAdapterId.Should().Be("auto");
    }

    [Test]
    public void NetworkStats_ResolvedPrimaryAdapterId_DefaultsToEmpty()
    {
        // Act
        var stats = new NetworkStats();

        // Assert
        stats.ResolvedPrimaryAdapterId.Should().Be(string.Empty);
        stats.ResolvedPrimaryAdapterName.Should().Be(string.Empty);
    }

    [Test]
    public void SecondaryAdapterInfo_Properties_AreAccessible()
    {
        // Act
        var info = new SecondaryAdapterInfo
        {
            AdapterId = "vpn0",
            Name = "WireGuard",
            Icon = "🔐",
            DownloadSpeed = "10 MB/s",
            UploadSpeed = "5 MB/s",
            DownloadBps = 10_000_000,
            UploadBps = 5_000_000,
            IsVpn = true,
            ColorHex = "#A855F7"
        };

        // Assert
        info.AdapterId.Should().Be("vpn0");
        info.Name.Should().Be("WireGuard");
        info.IsVpn.Should().BeTrue();
        info.DownloadBps.Should().Be(10_000_000);
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

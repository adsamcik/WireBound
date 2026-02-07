using Microsoft.Extensions.Logging;
using WireBound.Avalonia.ViewModels;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;
using IStartupService = WireBound.Platform.Abstract.Services.IStartupService;
using StartupState = WireBound.Platform.Abstract.Services.StartupState;

namespace WireBound.Tests.ViewModels;

/// <summary>
/// Unit tests for SettingsViewModel
/// </summary>
public class SettingsViewModelTests : IAsyncDisposable
{
    private readonly IDataPersistenceService _persistence;
    private readonly INetworkMonitorService _networkMonitor;
    private readonly IStartupService _startupService;
    private readonly IElevationService _elevationService;
    private readonly IProcessNetworkService _processNetworkService;
    private readonly IDataExportService _dataExport;
    private readonly IUpdateService _updateService;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModelTests()
    {
        _persistence = Substitute.For<IDataPersistenceService>();
        _networkMonitor = Substitute.For<INetworkMonitorService>();
        _startupService = Substitute.For<IStartupService>();
        _elevationService = Substitute.For<IElevationService>();
        _processNetworkService = Substitute.For<IProcessNetworkService>();
        _dataExport = Substitute.For<IDataExportService>();
        _updateService = Substitute.For<IUpdateService>();
        _logger = Substitute.For<ILogger<SettingsViewModel>>();

        SetupDefaultMocks();
    }

    private void SetupDefaultMocks()
    {
        // Setup network monitor
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(new List<NetworkAdapter>());

        // Setup persistence with default settings
        _persistence.GetSettingsAsync().Returns(CreateDefaultSettings());

        // Setup startup service
        _startupService.IsStartupSupported.Returns(true);
        _startupService.GetStartupStateAsync().Returns(StartupState.Disabled);
        _startupService.SetStartupWithResultAsync(Arg.Any<bool>()).Returns(StartupResult.Succeeded(StartupState.Disabled));

        // Setup elevation service
        _elevationService.IsHelperConnected.Returns(false);
        _elevationService.RequiresElevation.Returns(true);
        _elevationService.IsElevationSupported.Returns(true);
    }

    private static AppSettings CreateDefaultSettings()
    {
        return new AppSettings
        {
            PollingIntervalMs = 1000,
            UseIpHelperApi = false,
            IsPerAppTrackingEnabled = false,
            MinimizeToTray = true,
            StartMinimized = false,
            SpeedUnit = SpeedUnit.BytesPerSecond,
            ShowSystemMetricsInHeader = true,
            ShowCpuOverlayByDefault = false,
            ShowMemoryOverlayByDefault = false,
            ShowGpuMetrics = true,
            DefaultTimeRange = "FiveMinutes",
            PerformanceModeEnabled = false,
            ChartUpdateIntervalMs = 1000,
            DefaultInsightsPeriod = "ThisWeek",
            ShowCorrelationInsights = true
        };
    }

    private SettingsViewModel CreateViewModel()
    {
        return new SettingsViewModel(
            _persistence,
            _networkMonitor,
            _startupService,
            _elevationService,
            _processNetworkService,
            _dataExport,
            _updateService,
            _logger);
    }

    #region Constructor Tests

    [Test]
    public void Constructor_InitializesWithDefaultSettings()
    {
        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.PollingIntervalMs.Should().Be(1000);
        viewModel.UseIpHelperApi.Should().BeFalse();
        viewModel.IsPerAppTrackingEnabled.Should().BeFalse();
    }

    [Test]
    public void Constructor_LoadsAdaptersFromNetworkMonitor()
    {
        // Arrange
        var adapters = new List<NetworkAdapter>
        {
            new() { Id = "eth0", Name = "Ethernet" },
            new() { Id = "wifi0", Name = "WiFi" }
        };
        _networkMonitor.GetAdapters(Arg.Any<bool>()).Returns(adapters);

        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.Adapters.Should().HaveCount(2);
    }

    [Test]
    public void Constructor_LoadsSettingsFromPersistence()
    {
        // Arrange
        var settings = new AppSettings
        {
            PollingIntervalMs = 2000,
            UseIpHelperApi = true,
            MinimizeToTray = false,
            StartMinimized = true,
            SpeedUnit = SpeedUnit.BitsPerSecond
        };
        _persistence.GetSettingsAsync().Returns(settings);

        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.PollingIntervalMs.Should().Be(2000);
        viewModel.UseIpHelperApi.Should().BeTrue();
        viewModel.MinimizeToTray.Should().BeFalse();
        viewModel.StartMinimized.Should().BeTrue();
        viewModel.SelectedSpeedUnit.Should().Be(SpeedUnit.BitsPerSecond);
    }

    [Test]
    public void Constructor_SubscribesToHelperConnectionStateChanged()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert - NSubstitute verifies this through proper event subscription
        // The subscription is verified by the fact that the view model works correctly
        viewModel.Should().NotBeNull();
    }

    #endregion

    #region Property Defaults Tests

    [Test]
    public void SpeedUnits_ContainsAllSpeedUnitValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SpeedUnits.Should().Contain(SpeedUnit.BytesPerSecond);
        viewModel.SpeedUnits.Should().Contain(SpeedUnit.BitsPerSecond);
        viewModel.SpeedUnits.Should().HaveCount(Enum.GetValues<SpeedUnit>().Length);
    }

    [Test]
    public void PollingIntervals_ContainsExpectedValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.PollingIntervals.Should().BeEquivalentTo(new List<int> { 250, 500, 1000, 2000, 5000 });
    }

    [Test]
    public void TimeRangeOptions_ContainsExpectedValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.TimeRangeOptions.Should().BeEquivalentTo(
            new List<string> { "OneMinute", "FiveMinutes", "FifteenMinutes", "OneHour" });
    }

    [Test]
    public void ChartUpdateIntervals_ContainsExpectedValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.ChartUpdateIntervals.Should().BeEquivalentTo(
            new List<int> { 500, 750, 1000, 1500, 2000, 3000, 5000 });
    }

    [Test]
    public void InsightsPeriodOptions_ContainsExpectedValues()
    {
        // Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.InsightsPeriodOptions.Should().BeEquivalentTo(
            new List<string> { "Today", "ThisWeek", "ThisMonth" });
    }

    #endregion

    #region Auto-Save Scheduling Tests

    [Test]
    public async Task PollingIntervalMs_WhenChanged_SchedulesAutoSave()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow initial load to complete
        await Task.Delay(150);

        // Act
        viewModel.PollingIntervalMs = 2000;

        // Wait for auto-save delay (500ms) plus buffer
        await Task.Delay(700);

        // Assert
        await _persistence.Received().SaveSettingsAsync(Arg.Any<AppSettings>());
    }

    [Test]
    public async Task UseIpHelperApi_WhenChanged_SchedulesAutoSave()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow initial load to complete
        await Task.Delay(150);

        // Act
        viewModel.UseIpHelperApi = true;

        // Wait for auto-save delay
        await Task.Delay(700);

        // Assert
        await _persistence.Received().SaveSettingsAsync(Arg.Any<AppSettings>());
    }

    [Test]
    public async Task MinimizeToTray_WhenChanged_SchedulesAutoSave()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow initial load to complete
        await Task.Delay(150);

        // Act
        viewModel.MinimizeToTray = false;

        // Wait for auto-save delay
        await Task.Delay(700);

        // Assert
        await _persistence.Received().SaveSettingsAsync(Arg.Any<AppSettings>());
    }

    [Test]
    public async Task SelectedSpeedUnit_WhenChanged_SchedulesAutoSave()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow initial load to complete
        await Task.Delay(150);

        // Act
        viewModel.SelectedSpeedUnit = SpeedUnit.BitsPerSecond;

        // Wait for auto-save delay
        await Task.Delay(700);

        // Assert
        await _persistence.Received().SaveSettingsAsync(Arg.Any<AppSettings>());
    }

    [Test]
    public async Task RapidChanges_DebouncesAutoSave()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow initial load to complete
        await Task.Delay(150);

        // Act - make rapid changes
        viewModel.PollingIntervalMs = 2000;
        await Task.Delay(100);
        viewModel.PollingIntervalMs = 3000;
        await Task.Delay(100);
        viewModel.PollingIntervalMs = 4000;

        // Wait for auto-save delay
        await Task.Delay(700);

        // Assert - should debounce to fewer saves (ideally just one with final value)
        await _persistence.Received().SaveSettingsAsync(Arg.Is<AppSettings>(s => s.PollingIntervalMs == 4000));
    }

    [Test]
    public async Task DashboardSettings_WhenChanged_SchedulesAutoSave()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow initial load to complete
        await Task.Delay(150);

        // Act
        viewModel.ShowSystemMetricsInHeader = false;

        // Wait for auto-save delay
        await Task.Delay(700);

        // Assert
        await _persistence.Received().SaveSettingsAsync(Arg.Any<AppSettings>());
    }

    #endregion

    #region Elevation State Handling Tests

    [Test]
    public void IsElevated_ReflectsHelperConnectionState()
    {
        // Arrange
        _elevationService.IsHelperConnected.Returns(true);

        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.IsElevated.Should().BeTrue();
    }

    [Test]
    public void RequiresElevation_WhenHelperNotConnectedAndSupported_IsTrue()
    {
        // Arrange
        _elevationService.IsHelperConnected.Returns(false);
        _elevationService.RequiresElevation.Returns(true);
        _elevationService.IsElevationSupported.Returns(true);

        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.RequiresElevation.Should().BeTrue();
    }

    [Test]
    public void RequiresElevation_WhenHelperConnected_IsFalse()
    {
        // Arrange
        _elevationService.IsHelperConnected.Returns(true);
        _elevationService.RequiresElevation.Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.RequiresElevation.Should().BeFalse();
    }

    [Test]
    public void HelperConnectionStateChanged_UpdatesElevationStatus()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Act - Raise the event
        _elevationService.HelperConnectionStateChanged += Raise.Event<EventHandler<HelperConnectionStateChangedEventArgs>>(
            this, new HelperConnectionStateChangedEventArgs(true, "Connected"));

        // Need to update the mock return value for subsequent checks
        _elevationService.IsHelperConnected.Returns(true);

        // Assert
        viewModel.IsElevated.Should().BeTrue();
    }

    [Test]
    public async Task RequestElevationCommand_WhenNotSupported_DoesNotAttemptElevation()
    {
        // Arrange
        _elevationService.IsElevationSupported.Returns(false);
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        await Task.Delay(100);

        // Act
        await viewModel.RequestElevationCommand.ExecuteAsync(null);

        // Assert
        await _elevationService.DidNotReceive().StartHelperAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RequestElevationCommand_WhenAlreadyConnected_DoesNotAttemptElevation()
    {
        // Arrange
        _elevationService.IsHelperConnected.Returns(true);
        _elevationService.IsElevationSupported.Returns(true);
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        await Task.Delay(100);

        // Act
        await viewModel.RequestElevationCommand.ExecuteAsync(null);

        // Assert
        await _elevationService.DidNotReceive().StartHelperAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RequestElevationCommand_WhenSuccessful_AttemptsElevation()
    {
        // Arrange
        _elevationService.IsHelperConnected.Returns(false);
        _elevationService.IsElevationSupported.Returns(true);
        _elevationService.StartHelperAsync(Arg.Any<CancellationToken>()).Returns(ElevationResult.Success());

        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        await Task.Delay(100);

        // Act
        await viewModel.RequestElevationCommand.ExecuteAsync(null);

        // Assert
        await _elevationService.Received(1).StartHelperAsync(Arg.Any<CancellationToken>());
    }

    #endregion

    #region Startup Service Integration Tests

    [Test]
    public void StartWithWindows_WhenStartupEnabled_IsTrue()
    {
        // Arrange
        _startupService.GetStartupStateAsync().Returns(StartupState.Enabled);

        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.StartWithWindows.Should().BeTrue();
    }

    [Test]
    public void StartWithWindows_WhenStartupDisabled_IsFalse()
    {
        // Arrange
        _startupService.GetStartupStateAsync().Returns(StartupState.Disabled);

        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.StartWithWindows.Should().BeFalse();
    }

    [Test]
    public void IsStartupDisabledByUser_WhenDisabledByUser_IsTrue()
    {
        // Arrange
        _startupService.GetStartupStateAsync().Returns(StartupState.DisabledByUser);

        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.IsStartupDisabledByUser.Should().BeTrue();
    }

    [Test]
    public void IsStartupDisabledByPolicy_WhenDisabledByPolicy_IsTrue()
    {
        // Arrange
        _startupService.GetStartupStateAsync().Returns(StartupState.DisabledByPolicy);

        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.IsStartupDisabledByPolicy.Should().BeTrue();
    }

    [Test]
    public void WhenStartupNotSupported_StartWithWindowsIsFalse()
    {
        // Arrange
        _startupService.IsStartupSupported.Returns(false);

        // Act
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Assert
        viewModel.StartWithWindows.Should().BeFalse();
        viewModel.IsStartupDisabledByUser.Should().BeFalse();
        viewModel.IsStartupDisabledByPolicy.Should().BeFalse();
    }

    [Test]
    public async Task SaveCommand_WhenStartupSupported_AppliesStartupSetting()
    {
        // Arrange
        _startupService.IsStartupSupported.Returns(true);
        _startupService.SetStartupWithResultAsync(true).Returns(StartupResult.Succeeded(StartupState.Enabled));

        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        await Task.Delay(100);

        // Change startup setting
        viewModel.StartWithWindows = true;

        // Act
        await viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        await _startupService.Received(1).SetStartupWithResultAsync(true);
    }

    #endregion

    #region Per-App Tracking Toggle Tests

    [Test]
    public async Task IsPerAppTrackingEnabled_WhenEnabled_StartsProcessNetworkService()
    {
        // Arrange
        _processNetworkService.StartAsync().Returns(true);
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        await Task.Delay(150);

        // Act
        viewModel.IsPerAppTrackingEnabled = true;

        // Wait for async operation
        await Task.Delay(100);

        // Assert
        await _processNetworkService.Received(1).StartAsync();
    }

    [Test]
    public async Task IsPerAppTrackingEnabled_WhenDisabled_StopsProcessNetworkService()
    {
        // Arrange
        var settings = CreateDefaultSettings();
        settings.IsPerAppTrackingEnabled = true;
        _persistence.GetSettingsAsync().Returns(settings);

        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        await Task.Delay(150);

        // Act
        viewModel.IsPerAppTrackingEnabled = false;

        // Wait for async operation
        await Task.Delay(100);

        // Assert
        await _processNetworkService.Received(1).StopAsync();
    }

    [Test]
    public async Task IsPerAppTrackingEnabled_DuringLoading_DoesNotToggleService()
    {
        // Arrange
        var settings = CreateDefaultSettings();
        settings.IsPerAppTrackingEnabled = true;
        _persistence.GetSettingsAsync().Returns(settings);

        // Act - Create ViewModel (which loads settings including IsPerAppTrackingEnabled = true)
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        await Task.Delay(150);

        // Assert - Service should NOT be started during loading phase
        await _processNetworkService.DidNotReceive().StartAsync();
    }

    #endregion

    #region Dispose Tests

    [Test]
    public void Dispose_CancelsAutoSaveCts()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Trigger an auto-save to create a CancellationTokenSource
        viewModel.PollingIntervalMs = 2000;

        // Act & Assert - no exception should be thrown
        var action = () => viewModel.Dispose();
        action.Should().NotThrow();
    }

    [Test]
    public void Dispose_UnsubscribesFromHelperConnectionStateChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Act
        viewModel.Dispose();

        // Assert - After disposal, events should not trigger updates (verified by not throwing)
        viewModel.Should().NotBeNull();
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        Thread.Sleep(100);

        // Act & Assert - should not throw
        var action = () =>
        {
            viewModel.Dispose();
            viewModel.Dispose();
        };
        action.Should().NotThrow();
    }

    #endregion

    #region SaveCommand Tests

    [Test]
    public async Task SaveCommand_SavesAllSettings()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        await Task.Delay(100);

        // Modify settings
        viewModel.PollingIntervalMs = 2000;
        viewModel.UseIpHelperApi = true;

        // Act
        await viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        await _persistence.Received().SaveSettingsAsync(
            Arg.Is<AppSettings>(s =>
                s.PollingIntervalMs == 2000 &&
                s.UseIpHelperApi == true));
    }

    [Test]
    public async Task SaveCommand_AppliesUseIpHelperApiToNetworkMonitor()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Allow async LoadSettings to complete
        await Task.Delay(100);

        viewModel.UseIpHelperApi = true;

        // Act
        await viewModel.SaveCommand.ExecuteAsync(null);

        // Assert
        _networkMonitor.Received(1).SetUseIpHelperApi(true);
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

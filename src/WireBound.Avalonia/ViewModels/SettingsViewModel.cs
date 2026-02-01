using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;
using IStartupService = WireBound.Platform.Abstract.Services.IStartupService;
using StartupState = WireBound.Platform.Abstract.Services.StartupState;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Settings page
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IDataPersistenceService _persistence;
    private readonly INetworkMonitorService _networkMonitor;
    private readonly IStartupService _startupService;
    private readonly IElevationService _elevationService;
    private readonly IProcessNetworkService _processNetworkService;
    private readonly ILogger<SettingsViewModel>? _logger;
    private CancellationTokenSource? _autoSaveCts;
    private bool _isLoading = true;
    private const int AutoSaveDelayMs = 500;

    [ObservableProperty]
    private ObservableCollection<NetworkAdapter> _adapters = [];

    [ObservableProperty]
    private NetworkAdapter? _selectedAdapter;

    [ObservableProperty]
    private int _pollingIntervalMs = 1000;

    [ObservableProperty]
    private bool _useIpHelperApi;

    [ObservableProperty]
    private bool _isPerAppTrackingEnabled;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private SpeedUnit _selectedSpeedUnit = SpeedUnit.BytesPerSecond;

    public SpeedUnit[] SpeedUnits { get; } = Enum.GetValues<SpeedUnit>();

    // Dashboard Customization
    [ObservableProperty]
    private bool _showSystemMetricsInHeader = true;

    [ObservableProperty]
    private bool _showCpuOverlayByDefault;

    [ObservableProperty]
    private bool _showMemoryOverlayByDefault;

    [ObservableProperty]
    private bool _showGpuMetrics = true;

    [ObservableProperty]
    private string _defaultTimeRange = "FiveMinutes";

    public List<string> TimeRangeOptions { get; } = ["OneMinute", "FiveMinutes", "FifteenMinutes", "OneHour"];

    // Performance Mode
    [ObservableProperty]
    private bool _performanceModeEnabled;

    [ObservableProperty]
    private int _chartUpdateIntervalMs = 1000;

    public List<int> ChartUpdateIntervals { get; } = [500, 750, 1000, 1500, 2000, 3000, 5000];

    // Insights Page
    [ObservableProperty]
    private string _defaultInsightsPeriod = "ThisWeek";

    public List<string> InsightsPeriodOptions { get; } = ["Today", "ThisWeek", "ThisMonth"];

    [ObservableProperty]
    private bool _showCorrelationInsights = true;

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private bool _requiresElevation;

    [ObservableProperty]
    private bool _isRequestingElevation;

    [ObservableProperty]
    private bool _isStartupDisabledByUser;

    [ObservableProperty]
    private bool _isStartupDisabledByPolicy;

    public List<int> PollingIntervals { get; } = [250, 500, 1000, 2000, 5000];

    partial void OnSelectedAdapterChanged(NetworkAdapter? value) => ScheduleAutoSave();
    partial void OnPollingIntervalMsChanged(int value) => ScheduleAutoSave();
    partial void OnUseIpHelperApiChanged(bool value) => ScheduleAutoSave();
    partial void OnIsPerAppTrackingEnabledChanged(bool value)
    {
        ScheduleAutoSave();
        _ = ApplyPerAppTrackingSettingAsync(value);
    }

    private async Task ApplyPerAppTrackingSettingAsync(bool enabled)
    {
        if (_isLoading) return;

        if (enabled)
        {
            var started = await _processNetworkService.StartAsync();
            if (!started)
            {
                _logger?.LogWarning("Failed to start per-app network tracking service");
            }
        }
        else
        {
            await _processNetworkService.StopAsync();
        }
    }
    partial void OnStartWithWindowsChanged(bool value) => ScheduleAutoSave();
    partial void OnMinimizeToTrayChanged(bool value) => ScheduleAutoSave();
    partial void OnStartMinimizedChanged(bool value) => ScheduleAutoSave();
    partial void OnSelectedSpeedUnitChanged(SpeedUnit value) => ScheduleAutoSave();

    // Dashboard Customization auto-save handlers
    partial void OnShowSystemMetricsInHeaderChanged(bool value) => ScheduleAutoSave();
    partial void OnShowCpuOverlayByDefaultChanged(bool value) => ScheduleAutoSave();
    partial void OnShowMemoryOverlayByDefaultChanged(bool value) => ScheduleAutoSave();
    partial void OnShowGpuMetricsChanged(bool value) => ScheduleAutoSave();
    partial void OnDefaultTimeRangeChanged(string value) => ScheduleAutoSave();
    partial void OnPerformanceModeEnabledChanged(bool value) => ScheduleAutoSave();
    partial void OnChartUpdateIntervalMsChanged(int value) => ScheduleAutoSave();
    partial void OnDefaultInsightsPeriodChanged(string value) => ScheduleAutoSave();
    partial void OnShowCorrelationInsightsChanged(bool value) => ScheduleAutoSave();

    private void ScheduleAutoSave()
    {
        if (_isLoading) return;

        // Cancel any pending auto-save
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AutoSaveDelayMs, token);
                if (!token.IsCancellationRequested)
                {
                    await SaveAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation
            }
        }, token);
    }

    public SettingsViewModel(
        IDataPersistenceService persistence,
        INetworkMonitorService networkMonitor,
        IStartupService startupService,
        IElevationService elevationService,
        IProcessNetworkService processNetworkService,
        ILogger<SettingsViewModel>? logger = null)
    {
        _persistence = persistence;
        _networkMonitor = networkMonitor;
        _startupService = startupService;
        _elevationService = elevationService;
        _processNetworkService = processNetworkService;
        _logger = logger;

        LoadSettings();
    }

    private async void LoadSettings()
    {
        // Load adapters
        Adapters.Clear();
        foreach (var adapter in _networkMonitor.GetAdapters())
        {
            Adapters.Add(adapter);
        }

        // Load settings from database
        var settings = await _persistence.GetSettingsAsync();

        PollingIntervalMs = settings.PollingIntervalMs;
        UseIpHelperApi = settings.UseIpHelperApi;
        IsPerAppTrackingEnabled = settings.IsPerAppTrackingEnabled;
        MinimizeToTray = settings.MinimizeToTray;
        StartMinimized = settings.StartMinimized;
        SelectedSpeedUnit = settings.SpeedUnit;

        // Dashboard Customization
        ShowSystemMetricsInHeader = settings.ShowSystemMetricsInHeader;
        ShowCpuOverlayByDefault = settings.ShowCpuOverlayByDefault;
        ShowMemoryOverlayByDefault = settings.ShowMemoryOverlayByDefault;
        ShowGpuMetrics = settings.ShowGpuMetrics;
        DefaultTimeRange = settings.DefaultTimeRange;

        // Performance Mode
        PerformanceModeEnabled = settings.PerformanceModeEnabled;
        ChartUpdateIntervalMs = settings.ChartUpdateIntervalMs;

        // Insights Page
        DefaultInsightsPeriod = settings.DefaultInsightsPeriod;
        ShowCorrelationInsights = settings.ShowCorrelationInsights;

        // Load startup state from OS (not from saved settings)
        await LoadStartupStateAsync();

        // Apply speed unit setting globally
        WireBound.Core.Helpers.ByteFormatter.UseSpeedInBits = settings.SpeedUnit == SpeedUnit.BitsPerSecond;

        // Find matching adapter
        SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == settings.SelectedAdapterId);

        // Check elevation status using the platform service
        // IsElevated reflects whether the helper is connected (NOT whether the main app is elevated)
        IsElevated = _elevationService.IsHelperConnected;
        RequiresElevation = _elevationService.RequiresElevation && _elevationService.IsElevationSupported;

        // Subscribe to helper state changes
        _elevationService.HelperConnectionStateChanged += OnHelperConnectionStateChanged;

        _isLoading = false;
    }

    private void OnHelperConnectionStateChanged(object? sender, HelperConnectionStateChangedEventArgs e)
    {
        // Update UI state when helper connection changes
        IsElevated = e.IsConnected;
        RequiresElevation = !e.IsConnected && _elevationService.IsElevationSupported;
    }

    private async Task LoadStartupStateAsync()
    {
        if (!_startupService.IsStartupSupported)
        {
            StartWithWindows = false;
            IsStartupDisabledByUser = false;
            IsStartupDisabledByPolicy = false;
            return;
        }

        var state = await _startupService.GetStartupStateAsync();
        StartWithWindows = state == StartupState.Enabled;
        IsStartupDisabledByUser = state == StartupState.DisabledByUser;
        IsStartupDisabledByPolicy = state == StartupState.DisabledByPolicy;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            SelectedAdapterId = SelectedAdapter?.Id ?? "",
            PollingIntervalMs = PollingIntervalMs,
            UseIpHelperApi = UseIpHelperApi,
            IsPerAppTrackingEnabled = IsPerAppTrackingEnabled,
            MinimizeToTray = MinimizeToTray,
            StartMinimized = StartMinimized,
            StartWithWindows = StartWithWindows,
            SpeedUnit = SelectedSpeedUnit,

            // Dashboard Customization
            ShowSystemMetricsInHeader = ShowSystemMetricsInHeader,
            ShowCpuOverlayByDefault = ShowCpuOverlayByDefault,
            ShowMemoryOverlayByDefault = ShowMemoryOverlayByDefault,
            ShowGpuMetrics = ShowGpuMetrics,
            DefaultTimeRange = DefaultTimeRange,

            // Performance Mode
            PerformanceModeEnabled = PerformanceModeEnabled,
            ChartUpdateIntervalMs = ChartUpdateIntervalMs,

            // Insights Page
            DefaultInsightsPeriod = DefaultInsightsPeriod,
            ShowCorrelationInsights = ShowCorrelationInsights
        };

        // Apply speed unit setting globally
        WireBound.Core.Helpers.ByteFormatter.UseSpeedInBits = SelectedSpeedUnit == SpeedUnit.BitsPerSecond;

        await _persistence.SaveSettingsAsync(settings);

        // Apply settings
        _networkMonitor.SetUseIpHelperApi(UseIpHelperApi);

        // Apply startup setting to OS
        if (_startupService.IsStartupSupported)
        {
            var result = await _startupService.SetStartupWithResultAsync(StartWithWindows);
            // Update UI state based on actual result
            StartWithWindows = result.State == StartupState.Enabled;
            IsStartupDisabledByUser = result.State == StartupState.DisabledByUser;
            IsStartupDisabledByPolicy = result.State == StartupState.DisabledByPolicy;
        }
    }

    [RelayCommand]
    private async Task RequestElevationAsync()
    {
        if (!_elevationService.IsElevationSupported)
        {
            _logger?.LogWarning("Elevation requested but not supported on this platform");
            return;
        }

        if (_elevationService.IsHelperConnected)
        {
            _logger?.LogDebug("Helper already connected, no elevation needed");
            return;
        }

        IsRequestingElevation = true;
        try
        {
            _logger?.LogInformation("User requested to start elevated helper from Settings");

            // Start the minimal helper process (NOT elevate the entire app)
            var result = await _elevationService.StartHelperAsync();

            if (result.IsSuccess)
            {
                _logger?.LogInformation("Helper process started successfully");
                // Update elevation status
                IsElevated = _elevationService.IsHelperConnected;
                RequiresElevation = _elevationService.RequiresElevation;
            }
            else if (result.Status == ElevationStatus.Cancelled)
            {
                _logger?.LogInformation("User cancelled helper elevation request");
            }
            else
            {
                _logger?.LogWarning("Failed to start helper: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during helper start request");
        }
        finally
        {
            IsRequestingElevation = false;
        }
    }

    /// <summary>
    /// Disposes resources and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;

        _elevationService.HelperConnectionStateChanged -= OnHelperConnectionStateChanged;
    }
}

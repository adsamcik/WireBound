using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly INetworkPollingBackgroundService _pollingService;
    private readonly IStartupService _startupService;
    private readonly IElevationService _elevationService;
    private readonly IProcessNetworkService _processNetworkService;
    private readonly IDataExportService _dataExport;
    private readonly IUpdateService _updateService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SettingsViewModel>? _logger;
    private CancellationTokenSource? _autoSaveCts;
    private CancellationTokenSource? _downloadCts;
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

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    public string[] ThemeOptions { get; } = ["Dark", "Light", "System"];

    // Dashboard Customization
    [ObservableProperty]
    private bool _showSystemMetricsInHeader = true;

    [ObservableProperty]
    private bool _showCpuOverlayByDefault;

    [ObservableProperty]
    private bool _showMemoryOverlayByDefault;

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

    // Update Check
    [ObservableProperty]
    private bool _checkForUpdates = true;

    [ObservableProperty]
    private bool _autoDownloadUpdates = true;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private string? _latestVersion;

    [ObservableProperty]
    private string? _updateUrl;

    [ObservableProperty]
    private bool _isUpdateSupported;

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private bool _isReadyToRestart;

    [ObservableProperty]
    private string? _updateError;

    /// <summary>
    /// The pending update check result (holds native Velopack info for download/apply).
    /// </summary>
    public UpdateCheckResult? PendingUpdate { get; set; }

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

    /// <summary>Completes when async initialization finishes. Exposed for testability.</summary>
    public Task InitializationTask { get; }

    public List<int> PollingIntervals { get; } = [250, 500, 1000, 2000, 5000];

    partial void OnSelectedAdapterChanged(NetworkAdapter? value) => ScheduleAutoSave();

    partial void OnPollingIntervalMsChanged(int value)
    {
        if (value is < 100 or > 60_000)
        {
            PollingIntervalMs = Math.Clamp(value, 100, 60_000);
            return;
        }

        if (PerformanceModeEnabled)
        {
            _pollingService.SetAdaptivePolling(true, value);
        }
        ScheduleAutoSave();
    }
    partial void OnUseIpHelperApiChanged(bool value) => ScheduleAutoSave();
    internal Task? PendingPerAppTrackingTask { get; private set; }

    partial void OnIsPerAppTrackingEnabledChanged(bool value)
    {
        ScheduleAutoSave();
        PendingPerAppTrackingTask = ApplyPerAppTrackingSettingAsync(value);
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
    partial void OnDefaultTimeRangeChanged(string value) => ScheduleAutoSave();
    partial void OnPerformanceModeEnabledChanged(bool value)
    {
        _pollingService.SetAdaptivePolling(value, PollingIntervalMs);
        ScheduleAutoSave();
    }
    partial void OnChartUpdateIntervalMsChanged(int value) => ScheduleAutoSave();
    partial void OnDefaultInsightsPeriodChanged(string value) => ScheduleAutoSave();
    partial void OnShowCorrelationInsightsChanged(bool value) => ScheduleAutoSave();
    partial void OnCheckForUpdatesChanged(bool value) => ScheduleAutoSave();
    partial void OnAutoDownloadUpdatesChanged(bool value) => ScheduleAutoSave();

    partial void OnSelectedThemeChanged(string value)
    {
        ScheduleAutoSave();
        Helpers.ThemeHelper.ApplyTheme(value);
    }

    private void ScheduleAutoSave()
    {
        if (_isLoading) return;

        // Cancel and dispose any pending auto-save
        var oldCts = _autoSaveCts;
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        oldCts?.Cancel();
        oldCts?.Dispose();

        PendingAutoSaveTask = DelayedAutoSaveAsync(token);
    }

    private async Task DelayedAutoSaveAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(AutoSaveDelayMs), _timeProvider, token);
            if (!token.IsCancellationRequested)
            {
                await SaveAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Auto-save failed");
        }
    }

    internal Task? PendingAutoSaveTask { get; private set; }

    public SettingsViewModel(
        IDataPersistenceService persistence,
        INetworkMonitorService networkMonitor,
        INetworkPollingBackgroundService pollingService,
        IStartupService startupService,
        IElevationService elevationService,
        IProcessNetworkService processNetworkService,
        IDataExportService dataExport,
        IUpdateService updateService,
        TimeProvider timeProvider,
        ILogger<SettingsViewModel>? logger = null)
    {
        _persistence = persistence;
        _networkMonitor = networkMonitor;
        _pollingService = pollingService;
        _startupService = startupService;
        _elevationService = elevationService;
        _processNetworkService = processNetworkService;
        _dataExport = dataExport;
        _updateService = updateService;
        _timeProvider = timeProvider;
        _logger = logger;

        InitializationTask = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            // Load adapters
            Adapters.Clear();
            Adapters.Add(new NetworkAdapter
            {
                Id = NetworkMonitorConstants.AutoAdapterId,
                Name = "Auto",
                DisplayName = "🔄 Auto (detect primary)",
                Description = "Automatically detects the primary internet adapter via default gateway",
                AdapterType = NetworkAdapterType.Other,
                IsActive = true,
                Category = "Auto"
            });
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

            SelectedTheme = settings.Theme;

            // Dashboard Customization
            ShowSystemMetricsInHeader = settings.ShowSystemMetricsInHeader;
            ShowCpuOverlayByDefault = settings.ShowCpuOverlayByDefault;
            ShowMemoryOverlayByDefault = settings.ShowMemoryOverlayByDefault;
            DefaultTimeRange = settings.DefaultTimeRange;

            // Performance Mode
            PerformanceModeEnabled = settings.PerformanceModeEnabled;
            ChartUpdateIntervalMs = settings.ChartUpdateIntervalMs;

            // Insights Page
            DefaultInsightsPeriod = settings.DefaultInsightsPeriod;
            ShowCorrelationInsights = settings.ShowCorrelationInsights;

            // Updates
            CheckForUpdates = settings.CheckForUpdates;
            AutoDownloadUpdates = settings.AutoDownloadUpdates;
            IsUpdateSupported = _updateService.IsUpdateSupported;

            // Load startup state from OS (not from saved settings)
            await LoadStartupStateAsync();

            // Apply speed unit setting globally
            WireBound.Core.Helpers.ByteFormatter.UseSpeedInBits = settings.SpeedUnit == SpeedUnit.BitsPerSecond;

            // Find matching adapter, fall back to first if saved adapter no longer exists
            SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == settings.SelectedAdapterId)
                              ?? Adapters.FirstOrDefault();

            // Check elevation status using the platform service
            // IsElevated reflects whether the helper is connected (NOT whether the main app is elevated)
            IsElevated = _elevationService.IsHelperConnected;
            RequiresElevation = _elevationService.RequiresElevation && _elevationService.IsElevationSupported;

            // Subscribe to helper state changes
            _elevationService.HelperConnectionStateChanged += OnHelperConnectionStateChanged;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings");
        }
        finally
        {
            _isLoading = false;
        }
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

            Theme = SelectedTheme,

            // Dashboard Customization
            ShowSystemMetricsInHeader = ShowSystemMetricsInHeader,
            ShowCpuOverlayByDefault = ShowCpuOverlayByDefault,
            ShowMemoryOverlayByDefault = ShowMemoryOverlayByDefault,
            DefaultTimeRange = DefaultTimeRange,

            // Performance Mode
            PerformanceModeEnabled = PerformanceModeEnabled,
            ChartUpdateIntervalMs = ChartUpdateIntervalMs,

            // Insights Page
            DefaultInsightsPeriod = DefaultInsightsPeriod,
            ShowCorrelationInsights = ShowCorrelationInsights,

            // Updates
            CheckForUpdates = CheckForUpdates,
            AutoDownloadUpdates = AutoDownloadUpdates
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

    [ObservableProperty]
    private string? _exportStatus;

    [ObservableProperty]
    private bool _isExporting;

    [RelayCommand]
    private async Task ExportDailyDataAsync()
    {
        if (IsExporting) return;
        IsExporting = true;

        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "WireBound");
            Directory.CreateDirectory(folder);

            var filePath = Path.Combine(folder, $"wirebound-daily-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            var endDate = DateOnly.FromDateTime(DateTime.Today);
            var startDate = endDate.AddDays(-365);

            await _dataExport.ExportDailyUsageToCsvAsync(filePath, startDate, endDate);
            ExportStatus = $"Exported to {filePath}";
            _logger?.LogInformation("Daily data exported to {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            ExportStatus = $"Export failed: {ex.Message}";
            _logger?.LogError(ex, "Failed to export daily data");
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private async Task BackupDatabaseAsync()
    {
        if (IsExporting) return;
        IsExporting = true;

        try
        {
            var sourceDb = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WireBound",
                "wirebound.db");

            if (!File.Exists(sourceDb))
            {
                ExportStatus = "Database file not found";
                return;
            }

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "WireBound", "Backups");
            Directory.CreateDirectory(folder);

            var backupPath = Path.Combine(folder, $"wirebound-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db");

            // Use SQLite online backup to safely copy a database that may be in use
            using var sourceConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={sourceDb};Mode=ReadOnly");
            using var backupConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={backupPath}");
            await Task.Run(() =>
            {
                sourceConnection.Open();
                backupConnection.Open();
                sourceConnection.BackupDatabase(backupConnection);
            });

            ExportStatus = $"Backup saved to {backupPath}";
            _logger?.LogInformation("Database backed up to {BackupPath}", backupPath);
        }
        catch (Exception ex)
        {
            ExportStatus = $"Backup failed: {ex.Message}";
            _logger?.LogError(ex, "Failed to backup database");
        }
        finally
        {
            IsExporting = false;
        }
    }

    [RelayCommand]
    private void OpenUpdateUrl()
    {
        if (string.IsNullOrEmpty(UpdateUrl)) return;

        try
        {
            Process.Start(new ProcessStartInfo(UpdateUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open update URL");
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (PendingUpdate is null || IsDownloading) return;

        IsDownloading = true;
        UpdateError = null;
        DownloadProgress = 0;

        var oldCts = _downloadCts;
        _downloadCts = new CancellationTokenSource();
        oldCts?.Cancel();
        oldCts?.Dispose();

        try
        {
            await _updateService.DownloadUpdateAsync(
                PendingUpdate,
                progress => DownloadProgress = progress,
                _downloadCts.Token);

            IsReadyToRestart = true;
            IsDownloading = false;
            _logger?.LogInformation("Update downloaded, ready to restart");
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Update download cancelled by user");
            IsDownloading = false;
        }
        catch (Exception ex)
        {
            UpdateError = $"Download failed: {ex.Message}";
            _logger?.LogError(ex, "Failed to download update");
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void CancelDownload()
    {
        _downloadCts?.Cancel();
    }

    [RelayCommand]
    private void ApplyUpdateAndRestart()
    {
        if (PendingUpdate is null) return;

        try
        {
            _updateService.ApplyUpdateAndRestart(PendingUpdate);
        }
        catch (Exception ex)
        {
            UpdateError = $"Update failed: {ex.Message}";
            _logger?.LogError(ex, "Failed to apply update and restart");
        }
    }

    [RelayCommand]
    private async Task CheckForUpdateManuallyAsync()
    {
        try
        {
            UpdateError = null;
            var update = await _updateService.CheckForUpdateAsync();
            if (update is not null)
            {
                UpdateAvailable = true;
                LatestVersion = update.Version;
                UpdateUrl = update.ReleaseNotesUrl;
                PendingUpdate = update;
                IsUpdateSupported = _updateService.IsUpdateSupported;
            }
        }
        catch (Exception ex)
        {
            UpdateError = $"Check failed: {ex.Message}";
            _logger?.LogError(ex, "Manual update check failed");
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

        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = null;

        _elevationService.HelperConnectionStateChanged -= OnHelperConnectionStateChanged;
    }
}

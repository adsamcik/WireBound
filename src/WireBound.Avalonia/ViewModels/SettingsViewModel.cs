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
    private readonly IUiDispatcher? _dispatcher;
    private readonly ILogger<SettingsViewModel>? _logger;
    private bool _disposed;
    private CancellationTokenSource? _autoSaveCts;
    private CancellationTokenSource? _downloadCts;
    private bool _isLoading = true;
    private const int AutoSaveDelayMs = 500;

    // Tracks the OS startup state we last applied, so SaveAsync only re-invokes the
    // OS startup APIs (helper registration runs elevated -> UAC) when the value
    // actually changed, instead of on every auto-save.
    private bool _lastAppliedStartWithWindows;
    private bool _lastAppliedStartHelperWithSystem;

    // Last-committed (persisted) memory-alert values. SaveAsync writes these rather
    // than the live editable properties, so an unrelated auto-save does not leak
    // not-yet-saved memory-alert edits into the database.
    private bool _committedMemoryAlertsEnabled;
    private int _committedMemoryWarningThresholdPercent = 85;
    private int _committedMemoryCriticalThresholdPercent = 95;
    private int _committedMemoryFreeFloorMb = 2048;
    private int _committedMemoryAlertCooldownSeconds = 300;
    private int _committedMemoryAlertSustainedSeconds = 30;

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
    private TrayIconMode _selectedTrayIconMode = TrayIconMode.Traffic;

    public TrayIconMode[] TrayIconModes { get; } = Enum.GetValues<TrayIconMode>();

    /// <summary>
    /// Adapter options offered for the tray traffic graph: a synthetic
    /// "Follow monitored adapter" entry (Id = "") followed by the real adapters.
    /// Distinct from <see cref="Adapters"/>, which uses an "Auto (detect primary)" entry.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<NetworkAdapter> _trayAdapterOptions = [];

    [ObservableProperty]
    private NetworkAdapter? _selectedTrayAdapter;

    /// <summary>
    /// True when the tray icon is configured to show network traffic, gating the
    /// visibility of the tray adapter selector.
    /// </summary>
    public bool IsTrayTrafficMode => SelectedTrayIconMode == TrayIconMode.Traffic;

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

    // Memory Alerts
    [ObservableProperty]
    private bool _memoryAlertsEnabled;

    [ObservableProperty]
    private int _memoryWarningThresholdPercent = 85;

    [ObservableProperty]
    private int _memoryCriticalThresholdPercent = 95;

    [ObservableProperty]
    private int _memoryFreeFloorMb = 2048;

    [ObservableProperty]
    private int _memoryAlertCooldownSeconds = 300;

    [ObservableProperty]
    private int _memoryAlertSustainedSeconds = 30;

    /// <summary>
    /// True when memory-alert fields have been edited but not yet committed via the
    /// explicit Save. Memory alerts use explicit save (not auto-save) because applying
    /// them previously went through the shared auto-save, which re-runs the elevated
    /// helper-startup registration and triggered a UAC prompt on every keystroke.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveMemoryAlertsCommand))]
    private bool _hasUnsavedMemoryAlertChanges;

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

    [ObservableProperty]
    private bool _startHelperWithSystem;

    [ObservableProperty]
    private bool _isHelperStartupSupported;

    /// <summary>
    /// User-facing status of the elevation helper auto-start chain. Surfaces
    /// the difference between "registered to auto-start" vs "actually running"
    /// vs "connected and authenticated" so the user can diagnose problems
    /// without reading logs.
    /// </summary>
    [ObservableProperty]
    private HelperAutoStartStatus _helperAutoStartStatus;

    /// <summary>
    /// Set to true when <see cref="IHelperProcessManager.ValidateRegistrationAsync"/>
    /// reports the scheduled task / systemd unit has been tampered with. The UI
    /// should surface a warning banner inviting the user to "Repair" (which
    /// re-runs the one-time UAC/pkexec registration).
    /// </summary>
    [ObservableProperty]
    private bool _isHelperRegistrationTampered;

    /// <summary>
    /// Human-readable reason for the tamper detection, set alongside
    /// <see cref="IsHelperRegistrationTampered"/>.
    /// </summary>
    [ObservableProperty]
    private string? _helperRegistrationTamperReason;

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
    partial void OnStartHelperWithSystemChanged(bool value)
    {
        ScheduleAutoSave();
        UpdateHelperAutoStartStatus();
    }
    partial void OnMinimizeToTrayChanged(bool value) => ScheduleAutoSave();
    partial void OnSelectedTrayIconModeChanged(TrayIconMode value)
    {
        OnPropertyChanged(nameof(IsTrayTrafficMode));
        ScheduleAutoSave();
    }
    partial void OnSelectedTrayAdapterChanged(NetworkAdapter? value) => ScheduleAutoSave();
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
    partial void OnCheckForUpdatesChanged(bool value) => ScheduleAutoSave();
    partial void OnAutoDownloadUpdatesChanged(bool value) => ScheduleAutoSave();

    partial void OnMemoryAlertsEnabledChanged(bool value) => MarkMemoryAlertsDirty();
    partial void OnMemoryWarningThresholdPercentChanged(int value)
    {
        if (value is < 50 or > 99)
        {
            MemoryWarningThresholdPercent = Math.Clamp(value, 50, 99);
            return;
        }
        MarkMemoryAlertsDirty();
    }
    partial void OnMemoryCriticalThresholdPercentChanged(int value)
    {
        if (value is < 60 or > 99)
        {
            MemoryCriticalThresholdPercent = Math.Clamp(value, 60, 99);
            return;
        }
        MarkMemoryAlertsDirty();
    }
    partial void OnMemoryFreeFloorMbChanged(int value)
    {
        if (value is < 512 or > 16384)
        {
            MemoryFreeFloorMb = Math.Clamp(value, 512, 16384);
            return;
        }
        MarkMemoryAlertsDirty();
    }
    partial void OnMemoryAlertCooldownSecondsChanged(int value)
    {
        if (value is < 60 or > 3600)
        {
            MemoryAlertCooldownSeconds = Math.Clamp(value, 60, 3600);
            return;
        }
        MarkMemoryAlertsDirty();
    }
    partial void OnMemoryAlertSustainedSecondsChanged(int value)
    {
        if (value is < 5 or > 120)
        {
            MemoryAlertSustainedSeconds = Math.Clamp(value, 5, 120);
            return;
        }
        MarkMemoryAlertsDirty();
    }

    /// <summary>
    /// Flags the memory-alert fields as having unsaved edits. Memory alerts are not
    /// auto-saved — the user must click Save (see <see cref="SaveMemoryAlertsCommand"/>).
    /// </summary>
    private void MarkMemoryAlertsDirty()
    {
        if (_isLoading) return;
        HasUnsavedMemoryAlertChanges = true;
    }

    private bool CanSaveMemoryAlerts => HasUnsavedMemoryAlertChanges;

    /// <summary>
    /// Commits the edited memory-alert settings: persists them, pushes them to the live
    /// detector, and clears the unsaved-changes flag. Restores the previous committed
    /// snapshot if persistence fails so a later auto-save can't write a half-applied state.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveMemoryAlerts))]
    private async Task SaveMemoryAlertsAsync()
    {
        var previous = (
            _committedMemoryAlertsEnabled,
            _committedMemoryWarningThresholdPercent,
            _committedMemoryCriticalThresholdPercent,
            _committedMemoryFreeFloorMb,
            _committedMemoryAlertCooldownSeconds,
            _committedMemoryAlertSustainedSeconds);

        _committedMemoryAlertsEnabled = MemoryAlertsEnabled;
        _committedMemoryWarningThresholdPercent = MemoryWarningThresholdPercent;
        _committedMemoryCriticalThresholdPercent = MemoryCriticalThresholdPercent;
        _committedMemoryFreeFloorMb = MemoryFreeFloorMb;
        _committedMemoryAlertCooldownSeconds = MemoryAlertCooldownSeconds;
        _committedMemoryAlertSustainedSeconds = MemoryAlertSustainedSeconds;

        try
        {
            await SaveAsync();
        }
        catch
        {
            (_committedMemoryAlertsEnabled,
             _committedMemoryWarningThresholdPercent,
             _committedMemoryCriticalThresholdPercent,
             _committedMemoryFreeFloorMb,
             _committedMemoryAlertCooldownSeconds,
             _committedMemoryAlertSustainedSeconds) = previous;
            throw;
        }

        PushMemoryAlertSettings();
        HasUnsavedMemoryAlertChanges = false;
    }

    private void PushMemoryAlertSettings()
    {
        if (_isLoading) return;
        _pollingService.UpdateMemoryAlertSettings(
            MemoryAlertsEnabled,
            MemoryWarningThresholdPercent,
            MemoryCriticalThresholdPercent,
            MemoryFreeFloorMb,
            MemoryAlertCooldownSeconds,
            MemoryAlertSustainedSeconds);
    }

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
        ILogger<SettingsViewModel>? logger = null,
        IUiDispatcher? dispatcher = null)
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
        _dispatcher = dispatcher;

        _elevationService.HelperConnectionStateChanged += OnHelperConnectionStateChanged;

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
                DisplayName = "Auto (detect primary)",
                Description = "Automatically detects the primary internet adapter via default gateway",
                AdapterType = NetworkAdapterType.Other,
                IsActive = true,
                Category = "Auto"
            });
            foreach (var adapter in _networkMonitor.GetAdapters())
            {
                Adapters.Add(adapter);
            }

            // Tray traffic adapter options: a "Follow monitored adapter" sentinel
            // (Id = "") plus the real adapters. Kept separate from Adapters so the
            // tray's follow semantics aren't confused with the monitor's "Auto" entry.
            TrayAdapterOptions.Clear();
            TrayAdapterOptions.Add(new NetworkAdapter
            {
                Id = string.Empty,
                Name = "Follow monitored adapter",
                DisplayName = "Follow monitored adapter",
                Description = "Show traffic for whichever adapter the app is monitoring",
                AdapterType = NetworkAdapterType.Other,
                IsActive = true,
                Category = "Auto"
            });
            foreach (var adapter in _networkMonitor.GetAdapters())
            {
                TrayAdapterOptions.Add(adapter);
            }

            // Load settings from database
            var settings = await _persistence.GetSettingsAsync();

            PollingIntervalMs = settings.PollingIntervalMs;
            UseIpHelperApi = settings.UseIpHelperApi;
            IsPerAppTrackingEnabled = settings.IsPerAppTrackingEnabled;
            MinimizeToTray = settings.MinimizeToTray;
            SelectedTrayIconMode = settings.TrayIconMode;
            SelectedTrayAdapter = TrayAdapterOptions.FirstOrDefault(a => a.Id == settings.TrayTrafficAdapterId)
                                  ?? TrayAdapterOptions.FirstOrDefault();
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

            // Updates
            CheckForUpdates = settings.CheckForUpdates;
            AutoDownloadUpdates = settings.AutoDownloadUpdates;
            IsUpdateSupported = _updateService.IsUpdateSupported;

            // Memory Alerts
            MemoryAlertsEnabled = settings.MemoryAlertsEnabled;
            MemoryWarningThresholdPercent = settings.MemoryWarningThresholdPercent;
            MemoryCriticalThresholdPercent = settings.MemoryCriticalThresholdPercent;
            MemoryFreeFloorMb = settings.MemoryFreeFloorMb;
            MemoryAlertCooldownSeconds = settings.MemoryAlertCooldownSeconds;
            MemoryAlertSustainedSeconds = settings.MemoryAlertSustainedSeconds;

            // Snapshot the persisted values as the committed baseline; SaveAsync writes
            // these so unrelated auto-saves don't persist not-yet-saved memory edits.
            _committedMemoryAlertsEnabled = settings.MemoryAlertsEnabled;
            _committedMemoryWarningThresholdPercent = settings.MemoryWarningThresholdPercent;
            _committedMemoryCriticalThresholdPercent = settings.MemoryCriticalThresholdPercent;
            _committedMemoryFreeFloorMb = settings.MemoryFreeFloorMb;
            _committedMemoryAlertCooldownSeconds = settings.MemoryAlertCooldownSeconds;
            _committedMemoryAlertSustainedSeconds = settings.MemoryAlertSustainedSeconds;
            HasUnsavedMemoryAlertChanges = false;

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
        if (_dispatcher is not null)
        {
            _dispatcher.Post(() =>
            {
                IsElevated = e.IsConnected;
                RequiresElevation = !e.IsConnected && _elevationService.IsElevationSupported;
                UpdateHelperAutoStartStatus();
            }, UiDispatcherPriority.Background);
        }
        else
        {
            IsElevated = e.IsConnected;
            RequiresElevation = !e.IsConnected && _elevationService.IsElevationSupported;
            UpdateHelperAutoStartStatus();
        }
    }

    /// <summary>
    /// Recomputes the user-facing <see cref="HelperAutoStartStatus"/> from
    /// the current state of the elevation + startup services. Idempotent —
    /// safe to call from any state change.
    /// </summary>
    private void UpdateHelperAutoStartStatus()
    {
        if (!_elevationService.IsElevationSupported || !_startupService.IsHelperStartupSupported)
        {
            HelperAutoStartStatus = HelperAutoStartStatus.NotSupported;
            return;
        }

        if (IsHelperRegistrationTampered)
        {
            HelperAutoStartStatus = HelperAutoStartStatus.Tampered;
            return;
        }

        if (!StartHelperWithSystem)
        {
            HelperAutoStartStatus = HelperAutoStartStatus.Disabled;
            return;
        }

        // From here, auto-start is enabled. Refine: connected > running > registered.
        if (_elevationService.IsHelperConnected)
        {
            HelperAutoStartStatus = HelperAutoStartStatus.Connected;
            return;
        }

        // "Running but not connected" is rare in practice — the main app
        // authenticates immediately after the helper opens its socket. Best-
        // effort detection: the helper exposes its IPC endpoint as a file
        // on both platforms, so we can probe that without an auth attempt.
        try
        {
            var endpoint = OperatingSystem.IsWindows()
                ? @$"\\.\pipe\{IPC.IpcConstants.WindowsPipeName}"
                : IPC.IpcConstants.LinuxSocketPath;
            if (File.Exists(endpoint))
            {
                HelperAutoStartStatus = HelperAutoStartStatus.Running;
                return;
            }
        }
        catch { /* ignore — fall through to Registered */ }

        HelperAutoStartStatus = HelperAutoStartStatus.Registered;
    }

    /// <summary>
    /// Re-registers the helper auto-start hook (Windows Task Scheduler entry
    /// or Linux systemd unit) when tamper-detection has fired. Triggers a
    /// one-time UAC / pkexec prompt — this is the user explicitly repairing
    /// a flagged state, so the interactive prompt is acceptable.
    /// </summary>
    /// <remarks>
    /// Two-step (disable → re-enable) so the old tampered entry is removed
    /// before the new known-good one is written. If the re-enable step fails
    /// (most commonly because the user cancels UAC/pkexec), the user is left
    /// with NO registered entry at all — we explicitly flip the local toggle
    /// and clear the tamper banner to reflect that, so the UI doesn't lie
    /// about a tampered entry that no longer exists.
    /// </remarks>
    [RelayCommand]
    private async Task RepairHelperRegistrationAsync()
    {
        try
        {
            await _startupService.SetHelperStartupEnabledAsync(false);
            var ok = await _startupService.SetHelperStartupEnabledAsync(true);
            if (ok)
            {
                IsHelperRegistrationTampered = false;
                HelperRegistrationTamperReason = null;
                StartHelperWithSystem = true;
                _lastAppliedStartHelperWithSystem = true;
                UpdateHelperAutoStartStatus();
                _logger?.LogInformation("Helper auto-start registration repaired");
            }
            else
            {
                // Re-enable failed (typically user cancelled UAC/pkexec). The
                // old tampered entry was already deleted in the first step, so
                // the system is now in a clean "no registration" state. Sync
                // the UI to that reality instead of keeping the stale banner.
                StartHelperWithSystem = false;
                _lastAppliedStartHelperWithSystem = false;
                IsHelperRegistrationTampered = false;
                HelperRegistrationTamperReason =
                    "Repair cancelled. Auto-start is now disabled — re-enable the toggle to retry.";
                UpdateHelperAutoStartStatus();
                _logger?.LogWarning(
                    "Helper auto-start repair did not complete; previous tampered entry was removed, " +
                    "no new entry was created. Auto-start is now disabled.");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to repair helper registration");
        }
    }

    private async Task LoadStartupStateAsync()
    {
        if (!_startupService.IsStartupSupported)
        {
            StartWithWindows = false;
            IsStartupDisabledByUser = false;
            IsStartupDisabledByPolicy = false;
            _lastAppliedStartWithWindows = StartWithWindows;
            _lastAppliedStartHelperWithSystem = StartHelperWithSystem;
            return;
        }

        var state = await _startupService.GetStartupStateAsync();
        StartWithWindows = state == StartupState.Enabled;
        IsStartupDisabledByUser = state == StartupState.DisabledByUser;
        IsStartupDisabledByPolicy = state == StartupState.DisabledByPolicy;

        IsHelperStartupSupported = _startupService.IsHelperStartupSupported;
        if (IsHelperStartupSupported)
        {
            StartHelperWithSystem = await _startupService.IsHelperStartupEnabledAsync();
        }
        UpdateHelperAutoStartStatus();

        // Baseline the applied-state trackers to what the OS currently reports, so the
        // first save doesn't redundantly re-apply (and re-prompt for elevation).
        _lastAppliedStartWithWindows = StartWithWindows;
        _lastAppliedStartHelperWithSystem = StartHelperWithSystem;
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
            StartHelperWithSystem = StartHelperWithSystem,
            MinimizeToTray = MinimizeToTray,
            TrayIconMode = SelectedTrayIconMode,
            TrayTrafficAdapterId = SelectedTrayAdapter?.Id ?? string.Empty,
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

            // Updates
            CheckForUpdates = CheckForUpdates,
            AutoDownloadUpdates = AutoDownloadUpdates,

            // Memory Alerts — persist the committed snapshot, not the live editable
            // values, so unrelated auto-saves never write not-yet-saved edits.
            MemoryAlertsEnabled = _committedMemoryAlertsEnabled,
            MemoryWarningThresholdPercent = _committedMemoryWarningThresholdPercent,
            MemoryCriticalThresholdPercent = _committedMemoryCriticalThresholdPercent,
            MemoryFreeFloorMb = _committedMemoryFreeFloorMb,
            MemoryAlertCooldownSeconds = _committedMemoryAlertCooldownSeconds,
            MemoryAlertSustainedSeconds = _committedMemoryAlertSustainedSeconds
        };

        // Apply speed unit setting globally
        WireBound.Core.Helpers.ByteFormatter.UseSpeedInBits = SelectedSpeedUnit == SpeedUnit.BitsPerSecond;

        await _persistence.SaveSettingsAsync(settings);

        // Apply settings
        _networkMonitor.SetUseIpHelperApi(UseIpHelperApi);

        // Apply startup setting to OS only when it actually changed — re-applying on
        // every save is wasteful and (for the helper) re-runs an elevated registration
        // that prompts UAC each time.
        if (_startupService.IsStartupSupported && StartWithWindows != _lastAppliedStartWithWindows)
        {
            var result = await _startupService.SetStartupWithResultAsync(StartWithWindows);
            // Update UI state based on actual result
            StartWithWindows = result.State == StartupState.Enabled;
            IsStartupDisabledByUser = result.State == StartupState.DisabledByUser;
            IsStartupDisabledByPolicy = result.State == StartupState.DisabledByPolicy;
            _lastAppliedStartWithWindows = StartWithWindows;
        }

        // Apply helper startup setting to OS only when it changed (runs elevated -> UAC)
        if (_startupService.IsHelperStartupSupported && StartHelperWithSystem != _lastAppliedStartHelperWithSystem)
        {
            var helperResult = await _startupService.SetHelperStartupEnabledAsync(StartHelperWithSystem);
            if (!helperResult)
            {
                // Revert UI toggle if OS registration failed (e.g., user cancelled UAC)
                StartHelperWithSystem = await _startupService.IsHelperStartupEnabledAsync();
            }
            _lastAppliedStartHelperWithSystem = StartHelperWithSystem;
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
    /// Stops the elevated helper process. The helper exits gracefully on pipe
    /// disconnect, so this is a normal user-initiated shutdown — no UAC prompt
    /// required to stop something the user already authorized.
    /// </summary>
    [RelayCommand]
    private async Task StopElevationAsync()
    {
        if (!_elevationService.IsHelperConnected)
        {
            _logger?.LogDebug("Stop helper requested but no helper is currently connected");
            return;
        }

        IsRequestingElevation = true;
        try
        {
            _logger?.LogInformation("User requested to stop elevated helper from Settings");
            await _elevationService.StopHelperAsync();

            IsElevated = _elevationService.IsHelperConnected;
            RequiresElevation = _elevationService.RequiresElevation;

            _logger?.LogInformation("Helper process stopped");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during helper stop request");
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
        if (_disposed) return;
        _disposed = true;

        _autoSaveCts?.Cancel();
        _autoSaveCts?.Dispose();
        _autoSaveCts = null;

        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = null;

        _elevationService.HelperConnectionStateChanged -= OnHelperConnectionStateChanged;
    }
}

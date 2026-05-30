using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using WireBound.Avalonia.Messages;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Background service that polls network and system statistics at regular intervals
/// </summary>
public sealed class NetworkPollingBackgroundService : BackgroundService, INetworkPollingBackgroundService
{
    private readonly INetworkMonitorService _networkMonitor;
    private readonly ISystemMonitorService _systemMonitor;
    private readonly ISystemHistoryService _systemHistory;
    private readonly IDataPersistenceService _persistence;
    private readonly IProcessNetworkService? _processNetworkService;
    private readonly IResourceInsightsService? _resourceInsights;
    private readonly ITrayIconService _trayIcon;
    private readonly ILogger<NetworkPollingBackgroundService> _logger;
    private volatile int _pollIntervalMs = DefaultPollIntervalMs;
    private volatile int _saveIntervalSeconds = DefaultSaveIntervalSeconds;
    private readonly Stopwatch _saveStopwatch = new();
    private readonly Stopwatch _cleanupStopwatch = new();
    private readonly Stopwatch _snapshotFlushStopwatch = new();
    private readonly Stopwatch _systemStatsAggregationStopwatch = new();
    private readonly Stopwatch _retentionCleanupStopwatch = new();
    private readonly Stopwatch _liveResourcePollStopwatch = new();
    private readonly List<(long download, long upload, DateTime time)> _snapshotBuffer = new(SnapshotBufferCapacity);
    private readonly List<(double cpu, double memory, DateTime time)> _systemSnapshotBuffer = new(SnapshotBufferCapacity);
    private readonly object _snapshotBufferLock = new();
    private PeriodicTimer? _timer;
    private readonly object _timerLock = new();
    private TaskCompletionSource? _pollingStarted;

    /// <summary>
    /// Completes when the polling loop has started. Exposed for testability.
    /// </summary>
    internal Task? PollingStartedTask => _pollingStarted?.Task;

    // Adaptive polling state
    private volatile bool _adaptivePollingEnabled;
    private volatile int _basePollingIntervalMs = DefaultPollIntervalMs;
    private volatile int _consecutiveLowCpuTicks;

    // Memory pressure detection state
    private readonly IProcessResourceProvider? _processResourceProvider;
    private MemoryPressureLevel _currentPressureLevel = MemoryPressureLevel.Normal;
    private readonly Stopwatch _pressureSustainedStopwatch = new();
    private readonly Stopwatch _swapActiveStopwatch = new();
    private long _previousSwapUsedBytes = -1;
    private DateTime _lastMemoryAlertTime = DateTime.MinValue;

    // Memory alert settings (loaded from AppSettings on startup, updatable at runtime)
    private volatile bool _memoryAlertsEnabled;
    private volatile int _memoryWarningThreshold = 85;
    private volatile int _memoryCriticalThreshold = 95;
    private long _memoryFreeFloorBytes = 2048L * 1024 * 1024;
    private volatile int _memoryAlertCooldownSeconds = 300;
    private volatile int _memoryAlertSustainedSeconds = 30;

    #region Constants

    /// <summary>
    /// Default polling interval in milliseconds when no user setting is configured.
    /// </summary>
    private const int DefaultPollIntervalMs = 1000;

    /// <summary>
    /// Default interval between persisting stats to database when no user setting is configured.
    /// </summary>
    private const int DefaultSaveIntervalSeconds = 60;

    /// <summary>
    /// Delay before the first stats save after service startup, allowing initial readings to stabilize.
    /// </summary>
    private const int InitialSaveDelaySeconds = 5;

    /// <summary>
    /// Interval for flushing buffered speed snapshots to the database.
    /// Balances write performance with data freshness.
    /// </summary>
    private const int SnapshotFlushIntervalSeconds = 30;

    /// <summary>
    /// Interval for cleaning up old speed snapshots beyond the retention period.
    /// </summary>
    private const int CleanupIntervalMinutes = 5;

    /// <summary>
    /// Interval for aggregating system stats (CPU, memory) into hourly and daily summaries.
    /// </summary>
    private const int SystemStatsAggregationIntervalMinutes = 5;

    /// <summary>
    /// How often to poll per-app resource usage to feed
    /// <see cref="IResourceInsightsService.GetRollingCpuByApp(TimeSpan)"/>.
    /// 5 seconds = 12 samples per 60-second window — a good rolling-average
    /// resolution without the overhead of enumerating every process on the
    /// system each second.
    /// </summary>
    private const int LiveResourcePollIntervalSeconds = 5;

    /// <summary>
    /// Interval between data retention cleanup runs (once per hour is sufficient).
    /// </summary>
    private const int RetentionCleanupIntervalMinutes = 60;

    /// <summary>
    /// How long to retain detailed speed snapshots before cleanup.
    /// Older data is aggregated into hourly/daily summaries.
    /// </summary>
    private static readonly TimeSpan SpeedSnapshotRetention = TimeSpan.FromHours(2);

    /// <summary>
    /// Maximum number of speed snapshots to buffer before flushing to database.
    /// Sized to hold approximately <see cref="SnapshotFlushIntervalSeconds"/> worth of samples.
    /// </summary>
    private const int SnapshotBufferCapacity = 30;

    /// <summary>
    /// Minimum allowed polling interval to prevent excessive CPU usage.
    /// </summary>
    private const int MinPollingIntervalMs = 100;

    /// <summary>
    /// Maximum allowed polling interval (1 minute) to ensure reasonably responsive updates.
    /// </summary>
    private const int MaxPollingIntervalMs = 60000;

    /// <summary>
    /// Minimum allowed save interval to prevent excessive database writes.
    /// </summary>
    private const int MinSaveIntervalSeconds = 10;

    // Adaptive polling thresholds: (CPU%, interval multiplier)
    // Each tier increases the polling interval to reduce load
    private const double CpuThresholdTier1 = 70.0;  // Moderate load
    private const double CpuThresholdTier2 = 85.0;  // High load
    private const double CpuThresholdTier3 = 95.0;  // Critical load
    private const int AdaptiveMultiplierTier1 = 2;   // 2x base interval
    private const int AdaptiveMultiplierTier2 = 3;   // 3x base interval
    private const int AdaptiveMultiplierTier3 = 5;   // 5x base interval

    /// <summary>
    /// Number of consecutive ticks with low CPU before stepping down to a faster interval.
    /// Provides hysteresis to avoid oscillating between tiers.
    /// </summary>
    private const int HysteresisTickCount = 5;

    #endregion

    public NetworkPollingBackgroundService(
        INetworkMonitorService networkMonitor,
        ISystemMonitorService systemMonitor,
        ISystemHistoryService systemHistory,
        IDataPersistenceService persistence,
        ITrayIconService trayIcon,
        ILogger<NetworkPollingBackgroundService> logger,
        IProcessNetworkService? processNetworkService = null,
        IResourceInsightsService? resourceInsights = null,
        IProcessResourceProvider? processResourceProvider = null)
    {
        _networkMonitor = networkMonitor;
        _systemMonitor = systemMonitor;
        _systemHistory = systemHistory;
        _persistence = persistence;
        _processNetworkService = processNetworkService;
        _trayIcon = trayIcon;
        _logger = logger;
        _resourceInsights = resourceInsights;
        _processResourceProvider = processResourceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Network polling service starting");

        // Load settings
        var settings = await _persistence.GetSettingsAsync().ConfigureAwait(false);
        _pollIntervalMs = settings.PollingIntervalMs;
        _saveIntervalSeconds = settings.SaveIntervalSeconds;
        _networkMonitor.SetUseIpHelperApi(settings.UseIpHelperApi);

        _memoryAlertsEnabled = settings.MemoryAlertsEnabled;
        _memoryWarningThreshold = settings.MemoryWarningThresholdPercent;
        _memoryCriticalThreshold = settings.MemoryCriticalThresholdPercent;
        _memoryFreeFloorBytes = (long)settings.MemoryFreeFloorMb * 1024 * 1024;
        _memoryAlertCooldownSeconds = settings.MemoryAlertCooldownSeconds;
        _memoryAlertSustainedSeconds = settings.MemoryAlertSustainedSeconds;

        _logger.LogInformation("Polling interval: {PollIntervalMs}ms, Save interval: {SaveIntervalSeconds}s",
            _pollIntervalMs, _saveIntervalSeconds);

        // Initialize adaptive polling from settings
        if (settings.PerformanceModeEnabled)
        {
            _adaptivePollingEnabled = true;
            _basePollingIntervalMs = settings.PollingIntervalMs;
            _logger.LogInformation("Adaptive polling enabled at startup");
        }

        if (!string.IsNullOrEmpty(settings.SelectedAdapterId))
        {
            _networkMonitor.SetAdapter(settings.SelectedAdapterId);
        }

        // Perform an initial save after a short delay
        bool initialSaveDone = false;
        _saveStopwatch.Start();
        _cleanupStopwatch.Start();
        _snapshotFlushStopwatch.Start();
        _systemStatsAggregationStopwatch.Start();
        _retentionCleanupStopwatch.Start();
        _liveResourcePollStopwatch.Start();

        // Use PeriodicTimer for more consistent timing than Task.Delay
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_pollIntervalMs));

        _pollingStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pollingStarted.TrySetResult();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // WaitForNextTickAsync throws ObjectDisposedException when timer is
                    // replaced by UpdatePollingInterval — we catch that and loop with the new timer.
                    if (!await _timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                        break;
                }
                catch (ObjectDisposedException)
                {
                    // Timer was replaced by UpdatePollingInterval — continue with new instance
                    continue;
                }

                try
                {
                    // Poll network stats
                    _networkMonitor.Poll();

                    // Poll system stats (CPU, RAM)
                    _systemMonitor.Poll();

                    // Record system stats sample for historical tracking
                    var systemStats = _systemMonitor.GetCurrentStats();
                    await _systemHistory.RecordSampleAsync(systemStats).ConfigureAwait(false);

                    // Check for memory pressure and send alerts if thresholds exceeded
                    await CheckMemoryPressureAsync(systemStats, stoppingToken).ConfigureAwait(false);

                    // Adaptive polling: adjust interval based on CPU load
                    if (_adaptivePollingEnabled)
                    {
                        AdjustPollingForCpuLoad(systemStats.Cpu.UsagePercent);
                    }

                    // Update tray icon with current activity
                    var currentStats = _networkMonitor.GetCurrentStats();
                    _trayIcon.UpdateActivity(currentStats.DownloadSpeedBps, currentStats.UploadSpeedBps);

                    // Buffer speed snapshot for chart history
                    lock (_snapshotBufferLock)
                    {
                        _snapshotBuffer.Add((currentStats.DownloadSpeedBps, currentStats.UploadSpeedBps, DateTime.Now));
                        _systemSnapshotBuffer.Add((systemStats.Cpu.UsagePercent, systemStats.Memory.UsagePercent, DateTime.Now));
                    }

                    // Flush snapshot buffer periodically (every 30 seconds)
                    if (_snapshotFlushStopwatch.Elapsed.TotalSeconds >= SnapshotFlushIntervalSeconds)
                    {
                        await FlushSnapshotBufferAsync().ConfigureAwait(false);
                        _snapshotFlushStopwatch.Restart();
                    }

                    // Sample per-app CPU/RAM every ~5s so the Apps tab can show
                    // a rolling-60s average instead of just the date-range
                    // historical mean. Calling GetCurrentByAppAsync here also
                    // refreshes the service's CPU delta baseline — we MUST NOT
                    // also call it from any other place in the same tick or the
                    // delta math gets corrupted (see the comment on
                    // GetCurrentByCategoryAsync in ResourceInsightsService).
                    if (_resourceInsights != null
                        && _liveResourcePollStopwatch.Elapsed.TotalSeconds >= LiveResourcePollIntervalSeconds)
                    {
                        try
                        {
                            await _resourceInsights.GetCurrentByAppAsync(stoppingToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to sample live resource usage; will retry next tick.");
                        }
                        _liveResourcePollStopwatch.Restart();
                    }

                    // Aggregate system stats periodically (every 5 minutes)
                    if (_systemStatsAggregationStopwatch.Elapsed.TotalMinutes >= SystemStatsAggregationIntervalMinutes)
                    {
                        await _systemHistory.AggregateHourlyAsync().ConfigureAwait(false);
                        await _systemHistory.AggregateDailyAsync().ConfigureAwait(false);

                        // Record per-app resource snapshot for historical trending
                        if (_resourceInsights != null)
                        {
                            await _resourceInsights.RecordSnapshotAsync(stoppingToken).ConfigureAwait(false);
                        }

                        _systemStatsAggregationStopwatch.Restart();
                    }

                    // Periodic data retention cleanup (once per hour)
                    if (_retentionCleanupStopwatch.Elapsed.TotalMinutes >= RetentionCleanupIntervalMinutes)
                    {
                        await _persistence.CleanupOldDataAsync(settings.DataRetentionDays).ConfigureAwait(false);

                        // Per-app data maintenance: aggregate hourly→daily and clean up old records
                        if (settings.IsPerAppTrackingEnabled)
                        {
                            await _persistence.AggregateAppDataAsync(settings.AppDataAggregateAfterDays).ConfigureAwait(false);
                            if (settings.AppDataRetentionDays > 0)
                                await _persistence.CleanupOldAppDataAsync(settings.AppDataRetentionDays).ConfigureAwait(false);
                        }

                        _retentionCleanupStopwatch.Restart();
                    }

                    // Periodic cleanup of old speed and system snapshots
                    if (_cleanupStopwatch.Elapsed.TotalMinutes >= CleanupIntervalMinutes)
                    {
                        await _persistence.CleanupOldSpeedSnapshotsAsync(SpeedSnapshotRetention).ConfigureAwait(false);
                        await _persistence.CleanupOldSystemSnapshotsAsync(SpeedSnapshotRetention).ConfigureAwait(false);
                        _cleanupStopwatch.Restart();
                    }

                    // Initial save after short delay
                    if (!initialSaveDone && _saveStopwatch.Elapsed.TotalSeconds >= InitialSaveDelaySeconds)
                    {
                        var stats = _networkMonitor.GetCurrentStats();
                        await _persistence.SaveStatsAsync(stats).ConfigureAwait(false);
                        await SaveAppStatsIfRunningAsync().ConfigureAwait(false);
                        _saveStopwatch.Restart();
                        initialSaveDone = true;
                        _logger.LogInformation("Initial stats saved");
                    }
                    // Regular periodic saves
                    else if (initialSaveDone && _saveStopwatch.Elapsed.TotalSeconds >= _saveIntervalSeconds)
                    {
                        var stats = _networkMonitor.GetCurrentStats();
                        await _persistence.SaveStatsAsync(stats).ConfigureAwait(false);
                        await SaveAppStatsIfRunningAsync().ConfigureAwait(false);
                        _saveStopwatch.Restart();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error polling network stats");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        _logger.LogInformation("Network polling service stopping");

        // Dispose the polling timer
        _timer?.Dispose();

        // Flush any remaining buffered snapshots
        try
        {
            await FlushSnapshotBufferAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush snapshot buffer on shutdown");
        }

        // Final system stats aggregation before shutdown
        try
        {
            await _systemHistory.AggregateHourlyAsync().ConfigureAwait(false);
            _logger.LogInformation("Final system stats aggregation completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to aggregate system stats on shutdown");
        }

        // Final save before shutdown
        try
        {
            var finalStats = _networkMonitor.GetCurrentStats();
            await _persistence.SaveStatsAsync(finalStats).ConfigureAwait(false);
            await SaveAppStatsIfRunningAsync().ConfigureAwait(false);
            _logger.LogInformation("Final stats saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save final stats on shutdown");
        }
    }

    /// <inheritdoc />
    public void UpdatePollingInterval(int milliseconds)
    {
        if (milliseconds < MinPollingIntervalMs)
        {
            _logger.LogWarning("Polling interval {Interval}ms is too low, using minimum of {Min}ms", milliseconds, MinPollingIntervalMs);
            milliseconds = MinPollingIntervalMs;
        }
        else if (milliseconds > MaxPollingIntervalMs)
        {
            _logger.LogWarning("Polling interval {Interval}ms exceeds maximum, clamping to {Max}ms", milliseconds, MaxPollingIntervalMs);
            milliseconds = MaxPollingIntervalMs;
        }

        _pollIntervalMs = milliseconds;

        // Recreate the timer so the new interval takes effect immediately
        lock (_timerLock)
        {
            _timer?.Dispose();
            _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(milliseconds));
        }

        _logger.LogInformation("Polling interval updated to {Interval}ms", milliseconds);
    }

    /// <inheritdoc />
    public void UpdateSaveInterval(int seconds)
    {
        if (seconds < MinSaveIntervalSeconds)
        {
            _logger.LogWarning("Save interval {Interval}s is too low, using minimum of {Min}s", seconds, MinSaveIntervalSeconds);
            seconds = MinSaveIntervalSeconds;
        }

        _saveIntervalSeconds = seconds;
        _logger.LogInformation("Save interval updated to {Interval}s", seconds);
    }

    /// <inheritdoc />
    public void SetAdaptivePolling(bool enabled, int baseIntervalMs)
    {
        _adaptivePollingEnabled = enabled;
        _basePollingIntervalMs = Math.Max(baseIntervalMs, MinPollingIntervalMs);
        _consecutiveLowCpuTicks = 0;

        if (!enabled)
        {
            // Restore the base interval immediately
            UpdatePollingInterval(_basePollingIntervalMs);
            _logger.LogInformation("Adaptive polling disabled, restored base interval {Interval}ms", _basePollingIntervalMs);
        }
        else
        {
            _logger.LogInformation("Adaptive polling enabled with base interval {Interval}ms", _basePollingIntervalMs);
        }
    }

    /// <inheritdoc />
    public int CurrentPollingIntervalMs => _pollIntervalMs;

    /// <inheritdoc />
    public void UpdateMemoryAlertSettings(bool enabled, int warningThresholdPercent, int criticalThresholdPercent,
        int freeFloorMb, int cooldownSeconds, int sustainedSeconds)
    {
        _memoryAlertsEnabled = enabled;
        _memoryWarningThreshold = warningThresholdPercent;
        _memoryCriticalThreshold = criticalThresholdPercent;
        _memoryFreeFloorBytes = (long)freeFloorMb * 1024 * 1024;
        _memoryAlertCooldownSeconds = cooldownSeconds;
        _memoryAlertSustainedSeconds = sustainedSeconds;
        _logger.LogInformation("Memory alert settings updated: enabled={Enabled}, warning={Warning}%, critical={Critical}%",
            enabled, warningThresholdPercent, criticalThresholdPercent);
    }

    /// <summary>
    /// Adjusts the polling interval based on current CPU usage.
    /// Steps up immediately on high CPU, steps down with hysteresis to avoid oscillation.
    /// </summary>
    private void AdjustPollingForCpuLoad(double cpuPercent)
    {
        var baseInterval = _basePollingIntervalMs;

        int targetInterval;
        if (cpuPercent >= CpuThresholdTier3)
        {
            targetInterval = baseInterval * AdaptiveMultiplierTier3;
            _consecutiveLowCpuTicks = 0;
        }
        else if (cpuPercent >= CpuThresholdTier2)
        {
            targetInterval = baseInterval * AdaptiveMultiplierTier2;
            _consecutiveLowCpuTicks = 0;
        }
        else if (cpuPercent >= CpuThresholdTier1)
        {
            targetInterval = baseInterval * AdaptiveMultiplierTier1;
            _consecutiveLowCpuTicks = 0;
        }
        else
        {
            // CPU is below all thresholds — count hysteresis ticks before stepping down
            _consecutiveLowCpuTicks++;
            if (_consecutiveLowCpuTicks >= HysteresisTickCount)
            {
                targetInterval = baseInterval;
                _consecutiveLowCpuTicks = 0;
            }
            else
            {
                return; // Keep current interval during hysteresis
            }
        }

        targetInterval = Math.Min(targetInterval, MaxPollingIntervalMs);

        if (targetInterval != _pollIntervalMs)
        {
            _logger.LogDebug("Adaptive polling: CPU {Cpu:F0}%, interval {Old}ms → {New}ms",
                cpuPercent, _pollIntervalMs, targetInterval);
            UpdatePollingInterval(targetInterval);
        }
    }

    /// <summary>
    /// Flushes the buffered speed and system snapshots to the database.
    /// </summary>
    private async Task FlushSnapshotBufferAsync()
    {
        List<(long download, long upload, DateTime time)> snapshots;
        List<(double cpu, double memory, DateTime time)> systemSnapshots;
        lock (_snapshotBufferLock)
        {
            if (_snapshotBuffer.Count == 0 && _systemSnapshotBuffer.Count == 0)
                return;

            snapshots = _snapshotBuffer.ToList();
            _snapshotBuffer.Clear();
            systemSnapshots = _systemSnapshotBuffer.ToList();
            _systemSnapshotBuffer.Clear();
        }

        if (snapshots.Count > 0)
        {
            await _persistence.SaveSpeedSnapshotBatchAsync(snapshots).ConfigureAwait(false);
        }

        if (systemSnapshots.Count > 0)
        {
            await _persistence.SaveSystemSnapshotBatchAsync(systemSnapshots).ConfigureAwait(false);
        }

        _logger.LogDebug("Flushed {SpeedCount} speed and {SystemCount} system snapshots to database", snapshots.Count, systemSnapshots.Count);
    }

    /// <summary>
    /// Persists per-app network stats if the process network service is running.
    /// </summary>
    private async Task SaveAppStatsIfRunningAsync()
    {
        if (_processNetworkService is not { IsRunning: true })
            return;

        try
        {
            var appStats = _processNetworkService.GetCurrentStats();
            if (appStats.Count > 0)
            {
                await _persistence.SaveAppStatsAsync(appStats).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save per-app network stats");
        }
    }

    /// <summary>
    /// Checks current memory stats against configured thresholds and sends a
    /// <see cref="MemoryPressureMessage"/> via <see cref="WeakReferenceMessenger"/> when the
    /// pressure level changes. Ambient state (tray tinting, strip pulse) is always published
    /// regardless of the alerts-enabled toggle. Only alert persistence and logging are gated.
    /// Uses wall-clock time (not poll counts) for sustained-duration and swap-velocity tracking.
    /// </summary>
    private async Task CheckMemoryPressureAsync(SystemStats systemStats, CancellationToken cancellationToken)
    {
        var memory = systemStats.Memory;
        long swapUsedBytes = Math.Max(0L, memory.UsedVirtualBytes - memory.UsedBytes);

        // Track swap velocity using wall-clock time — active when swap grows > 10 MB/s sustained
        const long SwapVelocityThresholdBytesPerSecond = 10L * 1024 * 1024;
        bool swapActive = false;
        if (_previousSwapUsedBytes >= 0 && _swapActiveStopwatch.IsRunning)
        {
            double elapsedSeconds = _swapActiveStopwatch.Elapsed.TotalSeconds;
            if (elapsedSeconds > 0)
            {
                long swapDelta = swapUsedBytes - _previousSwapUsedBytes;
                double swapBytesPerSecond = swapDelta / elapsedSeconds;

                if (swapBytesPerSecond > SwapVelocityThresholdBytesPerSecond)
                {
                    swapActive = true;
                }
                else
                {
                    _swapActiveStopwatch.Restart();
                }
            }
        }
        else
        {
            _swapActiveStopwatch.Restart();
        }

        // Only count swap as "active" after 10+ real seconds of sustained growth
        if (swapActive && _swapActiveStopwatch.Elapsed.TotalSeconds < 10)
            swapActive = false;

        _previousSwapUsedBytes = swapUsedBytes;

        // Determine candidate pressure level from current thresholds
        bool belowFreeFloor = memory.AvailableBytes < _memoryFreeFloorBytes;
        MemoryPressureLevel candidateLevel;
        if (memory.UsagePercent > _memoryCriticalThreshold && belowFreeFloor)
            candidateLevel = MemoryPressureLevel.Critical;
        else if (memory.UsagePercent > _memoryWarningThreshold && belowFreeFloor)
            candidateLevel = MemoryPressureLevel.Warning;
        else
            candidateLevel = MemoryPressureLevel.Normal;

        // Track sustained pressure using wall-clock elapsed time (not poll count)
        if (candidateLevel != MemoryPressureLevel.Normal)
        {
            if (!_pressureSustainedStopwatch.IsRunning)
                _pressureSustainedStopwatch.Start();
        }
        else
        {
            _pressureSustainedStopwatch.Reset();
        }

        double sustainedSeconds = _pressureSustainedStopwatch.Elapsed.TotalSeconds;

        // Require sustained pressure before escalating; instant recovery always passes through
        if (candidateLevel != MemoryPressureLevel.Normal && sustainedSeconds < _memoryAlertSustainedSeconds)
        {
            // Still publish ambient state for tray/strip even before sustained threshold is met
            if (candidateLevel != _currentPressureLevel)
            {
                _currentPressureLevel = candidateLevel;
                var ambientExplanation = BuildMemoryExplanation(memory.UsagePercent, memory.AvailableBytes, swapUsedBytes, swapActive, sustainedSeconds);
                WeakReferenceMessenger.Default.Send(new MemoryPressureMessage(
                    candidateLevel, memory.UsagePercent, memory.AvailableBytes, swapUsedBytes,
                    ambientExplanation, null));
            }
            return;
        }

        // Nothing to do when pressure level hasn't changed
        if (candidateLevel == _currentPressureLevel)
            return;

        bool transitioningUp = candidateLevel > _currentPressureLevel;

        // Cooldown gate prevents alert fatigue on repeated upward transitions (only when alerts enabled)
        if (_memoryAlertsEnabled && transitioningUp &&
            (DateTime.Now - _lastMemoryAlertTime).TotalSeconds < _memoryAlertCooldownSeconds)
        {
            // Still publish ambient state update even during cooldown
            _currentPressureLevel = candidateLevel;
            var cooldownExplanation = BuildMemoryExplanation(memory.UsagePercent, memory.AvailableBytes, swapUsedBytes, swapActive, sustainedSeconds);
            WeakReferenceMessenger.Default.Send(new MemoryPressureMessage(
                candidateLevel, memory.UsagePercent, memory.AvailableBytes, swapUsedBytes,
                cooldownExplanation, null));
            return;
        }

        // Re-verify on upward transitions to guard against stale readings from a spike that already recovered
        if (transitioningUp)
        {
            var freshStats = _systemMonitor.GetCurrentStats();
            bool stillPressured = candidateLevel == MemoryPressureLevel.Critical
                ? freshStats.Memory.UsagePercent > _memoryCriticalThreshold && freshStats.Memory.AvailableBytes < _memoryFreeFloorBytes
                : freshStats.Memory.UsagePercent > _memoryWarningThreshold && freshStats.Memory.AvailableBytes < _memoryFreeFloorBytes;
            if (!stillPressured)
                return;
        }

        // Collect top memory consumers for blame attribution on upward transitions
        IReadOnlyList<ProcessMemoryInfo>? topProcesses = null;
        if (transitioningUp && _processResourceProvider != null)
        {
            try
            {
                var processes = await _processResourceProvider.GetProcessResourceDataAsync(cancellationToken).ConfigureAwait(false);
                topProcesses = processes
                    .GroupBy(p => p.ProcessName, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new ProcessMemoryInfo(g.Key, g.Sum(p => p.WorkingSetBytes), g.Count()))
                    .OrderByDescending(p => p.MemoryBytes)
                    .Take(5)
                    .ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to collect process memory data for blame attribution");
            }
        }

        _currentPressureLevel = candidateLevel;
        if (transitioningUp)
            _lastMemoryAlertTime = DateTime.Now;

        var explanation = BuildMemoryExplanation(memory.UsagePercent, memory.AvailableBytes, swapUsedBytes, swapActive, sustainedSeconds);

        // Always publish ambient state (tray tinting, strip pulse) regardless of alerts toggle
        WeakReferenceMessenger.Default.Send(new MemoryPressureMessage(
            candidateLevel,
            memory.UsagePercent,
            memory.AvailableBytes,
            swapUsedBytes,
            explanation,
            topProcesses));

        // Only persist events and log when alerts are explicitly enabled
        if (!_memoryAlertsEnabled)
            return;

        _logger.LogInformation("Memory pressure changed to {Level}: {Explanation}", candidateLevel, explanation);

        // Persist event for historical analysis
        try
        {
            var topProcessesSummary = topProcesses != null
                ? string.Join(";", topProcesses.Select(p => $"{p.ProcessName}:{ByteFormatter.FormatBytes(p.MemoryBytes)}"))
                : string.Empty;

            await _persistence.SaveMemoryPressureEventAsync(new MemoryPressureEvent
            {
                Timestamp = DateTime.Now,
                Level = candidateLevel,
                UsagePercent = memory.UsagePercent,
                AvailableBytes = memory.AvailableBytes,
                SwapUsedBytes = swapUsedBytes,
                TopProcesses = topProcessesSummary,
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to persist memory pressure event");
        }
    }

    private string BuildMemoryExplanation(double usagePercent, long availableBytes, long swapUsedBytes, bool swapActive, double sustainedSeconds)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"RAM {usagePercent:F0}% for {sustainedSeconds:F0}s ({ByteFormatter.FormatBytes(availableBytes)} free");
        if (swapActive)
            sb.Append($", swap active {ByteFormatter.FormatBytes(swapUsedBytes)}");
        sb.Append(')');
        return sb.ToString();
    }
}

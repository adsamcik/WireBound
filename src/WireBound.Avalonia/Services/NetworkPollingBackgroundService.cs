using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using WireBound.Core.Services;

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
    private readonly IResourceInsightsService? _resourceInsights;
    private readonly ITrayIconService _trayIcon;
    private readonly ILogger<NetworkPollingBackgroundService> _logger;
    private volatile int _pollIntervalMs = DefaultPollIntervalMs;
    private volatile int _saveIntervalSeconds = DefaultSaveIntervalSeconds;
    private readonly Stopwatch _saveStopwatch = new();
    private readonly Stopwatch _cleanupStopwatch = new();
    private readonly Stopwatch _snapshotFlushStopwatch = new();
    private readonly Stopwatch _systemStatsAggregationStopwatch = new();
    private readonly List<(long download, long upload, DateTime time)> _snapshotBuffer = new(SnapshotBufferCapacity);
    private readonly object _snapshotBufferLock = new();
    private PeriodicTimer? _timer;
    private readonly object _timerLock = new();

    // Adaptive polling state
    private volatile bool _adaptivePollingEnabled;
    private volatile int _basePollingIntervalMs = DefaultPollIntervalMs;
    private volatile int _consecutiveLowCpuTicks;

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
        IResourceInsightsService? resourceInsights = null)
    {
        _networkMonitor = networkMonitor;
        _systemMonitor = systemMonitor;
        _systemHistory = systemHistory;
        _persistence = persistence;
        _trayIcon = trayIcon;
        _logger = logger;
        _resourceInsights = resourceInsights;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Network polling service starting");

        // Load settings
        var settings = await _persistence.GetSettingsAsync().ConfigureAwait(false);
        _pollIntervalMs = settings.PollingIntervalMs;
        _saveIntervalSeconds = settings.SaveIntervalSeconds;
        _networkMonitor.SetUseIpHelperApi(settings.UseIpHelperApi);

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

        // Use PeriodicTimer for more consistent timing than Task.Delay
        _timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_pollIntervalMs));

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
                    }

                    // Flush snapshot buffer periodically (every 30 seconds)
                    if (_snapshotFlushStopwatch.Elapsed.TotalSeconds >= SnapshotFlushIntervalSeconds)
                    {
                        await FlushSnapshotBufferAsync().ConfigureAwait(false);
                        _snapshotFlushStopwatch.Restart();
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

                    // Periodic cleanup of old speed snapshots
                    if (_cleanupStopwatch.Elapsed.TotalMinutes >= CleanupIntervalMinutes)
                    {
                        await _persistence.CleanupOldSpeedSnapshotsAsync(SpeedSnapshotRetention).ConfigureAwait(false);
                        _cleanupStopwatch.Restart();
                    }

                    // Initial save after short delay
                    if (!initialSaveDone && _saveStopwatch.Elapsed.TotalSeconds >= InitialSaveDelaySeconds)
                    {
                        var stats = _networkMonitor.GetCurrentStats();
                        await _persistence.SaveStatsAsync(stats).ConfigureAwait(false);
                        _saveStopwatch.Restart();
                        initialSaveDone = true;
                        _logger.LogInformation("Initial stats saved");
                    }
                    // Regular periodic saves
                    else if (initialSaveDone && _saveStopwatch.Elapsed.TotalSeconds >= _saveIntervalSeconds)
                    {
                        var stats = _networkMonitor.GetCurrentStats();
                        await _persistence.SaveStatsAsync(stats).ConfigureAwait(false);
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
    /// Flushes the buffered speed snapshots to the database.
    /// </summary>
    private async Task FlushSnapshotBufferAsync()
    {
        List<(long download, long upload, DateTime time)> snapshots;
        lock (_snapshotBufferLock)
        {
            if (_snapshotBuffer.Count == 0)
                return;

            snapshots = _snapshotBuffer.ToList();
            _snapshotBuffer.Clear();
        }

        await _persistence.SaveSpeedSnapshotBatchAsync(snapshots).ConfigureAwait(false);
        _logger.LogDebug("Flushed {Count} speed snapshots to database", snapshots.Count);
    }
}

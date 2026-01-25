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
    private readonly ITrayIconService _trayIcon;
    private readonly ILogger<NetworkPollingBackgroundService> _logger;
    private volatile int _pollIntervalMs = 1000;
    private volatile int _saveIntervalSeconds = 60;
    private readonly Stopwatch _saveStopwatch = new();
    private readonly Stopwatch _cleanupStopwatch = new();
    private readonly Stopwatch _snapshotFlushStopwatch = new();
    private readonly Stopwatch _systemStatsAggregationStopwatch = new();
    private readonly List<(long download, long upload, DateTime time)> _snapshotBuffer = new(30);
    private const int SnapshotFlushIntervalSeconds = 30;
    private const int CleanupIntervalMinutes = 5;
    private const int SystemStatsAggregationIntervalMinutes = 5;
    private static readonly TimeSpan SpeedSnapshotRetention = TimeSpan.FromHours(2);

    public NetworkPollingBackgroundService(
        INetworkMonitorService networkMonitor,
        ISystemMonitorService systemMonitor,
        ISystemHistoryService systemHistory,
        IDataPersistenceService persistence,
        ITrayIconService trayIcon,
        ILogger<NetworkPollingBackgroundService> logger)
    {
        _networkMonitor = networkMonitor;
        _systemMonitor = systemMonitor;
        _systemHistory = systemHistory;
        _persistence = persistence;
        _trayIcon = trayIcon;
        _logger = logger;
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

        if (!string.IsNullOrEmpty(settings.SelectedAdapterId))
        {
            _networkMonitor.SetAdapter(settings.SelectedAdapterId);
        }

        // Perform an initial save after a short delay
        bool initialSaveDone = false;
        const int initialSaveDelaySeconds = 5;
        _saveStopwatch.Start();
        _cleanupStopwatch.Start();
        _snapshotFlushStopwatch.Start();
        _systemStatsAggregationStopwatch.Start();

        // Use PeriodicTimer for more consistent timing than Task.Delay
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_pollIntervalMs));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    // Poll network stats
                    _networkMonitor.Poll();
                    
                    // Poll system stats (CPU, RAM)
                    _systemMonitor.Poll();
                    
                    // Record system stats sample for historical tracking
                    var systemStats = _systemMonitor.GetCurrentStats();
                    await _systemHistory.RecordSampleAsync(systemStats).ConfigureAwait(false);
                    
                    // Update tray icon with current activity
                    var currentStats = _networkMonitor.GetCurrentStats();
                    _trayIcon.UpdateActivity(currentStats.DownloadSpeedBps, currentStats.UploadSpeedBps);

                    // Buffer speed snapshot for chart history
                    _snapshotBuffer.Add((currentStats.DownloadSpeedBps, currentStats.UploadSpeedBps, DateTime.Now));

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
                        _systemStatsAggregationStopwatch.Restart();
                    }

                    // Periodic cleanup of old speed snapshots
                    if (_cleanupStopwatch.Elapsed.TotalMinutes >= CleanupIntervalMinutes)
                    {
                        await _persistence.CleanupOldSpeedSnapshotsAsync(SpeedSnapshotRetention).ConfigureAwait(false);
                        _cleanupStopwatch.Restart();
                    }

                    // Initial save after short delay
                    if (!initialSaveDone && _saveStopwatch.Elapsed.TotalSeconds >= initialSaveDelaySeconds)
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
        const int MinPollingIntervalMs = 100;
        const int MaxPollingIntervalMs = 60000;

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
        _logger.LogInformation("Polling interval updated to {Interval}ms", milliseconds);
    }

    /// <inheritdoc />
    public void UpdateSaveInterval(int seconds)
    {
        if (seconds < 10)
        {
            _logger.LogWarning("Save interval {Interval}s is too low, using minimum of 10s", seconds);
            seconds = 10;
        }

        _saveIntervalSeconds = seconds;
        _logger.LogInformation("Save interval updated to {Interval}s", seconds);
    }

    /// <summary>
    /// Flushes the buffered speed snapshots to the database.
    /// </summary>
    private async Task FlushSnapshotBufferAsync()
    {
        if (_snapshotBuffer.Count == 0)
            return;

        // Copy and clear buffer atomically
        var snapshots = _snapshotBuffer.ToList();
        _snapshotBuffer.Clear();

        await _persistence.SaveSpeedSnapshotBatchAsync(snapshots).ConfigureAwait(false);
        _logger.LogDebug("Flushed {Count} speed snapshots to database", snapshots.Count);
    }
}

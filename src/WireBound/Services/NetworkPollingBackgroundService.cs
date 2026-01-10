using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WireBound.Models;

namespace WireBound.Services;

/// <summary>
/// Background service that polls network statistics at regular intervals
/// </summary>
public class NetworkPollingBackgroundService : BackgroundService
{
    private readonly INetworkMonitorService _networkMonitor;
    private readonly IDataPersistenceService _persistence;
    private readonly ITelemetryService _telemetry;
    private readonly ILogger<NetworkPollingBackgroundService> _logger;
    private int _pollIntervalMs = 1000;
    private int _saveIntervalSeconds = 60;
    private DateTime _lastSaveTime = DateTime.Now;
    private DateTime _sessionStartTime = DateTime.Now;
    private long _peakDownloadSpeed = 0;
    private long _peakUploadSpeed = 0;

    public NetworkPollingBackgroundService(
        INetworkMonitorService networkMonitor,
        IDataPersistenceService persistence,
        ITelemetryService telemetry,
        ILogger<NetworkPollingBackgroundService> logger)
    {
        _networkMonitor = networkMonitor;
        _persistence = persistence;
        _telemetry = telemetry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Network polling service starting");

        // Log session start event
        await _telemetry.LogEventAsync(
            TelemetryCategory.App,
            "SessionStart",
            "WireBound monitoring session started",
            metadata: $"{{\"startTime\": \"{_sessionStartTime:O}\"}}");

        // Load settings
        var settings = await _persistence.GetSettingsAsync();
        _pollIntervalMs = settings.PollingIntervalMs;
        _saveIntervalSeconds = settings.SaveIntervalSeconds;
        _networkMonitor.SetUseIpHelperApi(settings.UseIpHelperApi);

        _logger.LogInformation("Polling interval: {PollIntervalMs}ms, Save interval: {SaveIntervalSeconds}s, IP Helper API: {UseIpHelperApi}",
            _pollIntervalMs, _saveIntervalSeconds, settings.UseIpHelperApi);

        if (!string.IsNullOrEmpty(settings.SelectedAdapterId))
        {
            _networkMonitor.SetAdapter(settings.SelectedAdapterId);
        }

        // Periodic cleanup and aggregation
        var lastCleanupTime = DateTime.Now;
        var lastAggregationTime = DateTime.Now;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Poll network stats
                _networkMonitor.Poll();
                var stats = _networkMonitor.GetCurrentStats();

                // Track peak speeds and log significant spikes
                await TrackPeakSpeedsAsync(stats);

                // Save to database periodically
                if ((DateTime.Now - _lastSaveTime).TotalSeconds >= _saveIntervalSeconds)
                {
                    await _persistence.SaveStatsAsync(stats);
                    _lastSaveTime = DateTime.Now;
                }

                // Cleanup old data once per day
                if ((DateTime.Now - lastCleanupTime).TotalHours >= 24)
                {
                    await _persistence.CleanupOldDataAsync(settings.DataRetentionDays);
                    await _telemetry.CleanupOldEventsAsync(settings.DataRetentionDays);
                    lastCleanupTime = DateTime.Now;
                }

                // Aggregate weekly data every 6 hours
                if ((DateTime.Now - lastAggregationTime).TotalHours >= 6)
                {
                    await _telemetry.AggregateWeeklyDataAsync();
                    lastAggregationTime = DateTime.Now;
                }

                await Task.Delay(_pollIntervalMs, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log error but continue running
                _logger.LogError(ex, "Error during network polling");
                await _telemetry.LogEventAsync(
                    TelemetryCategory.Error,
                    "PollingError",
                    $"Error during network polling: {ex.Message}");
                await Task.Delay(5000, stoppingToken); // Wait longer on error
            }
        }

        _logger.LogInformation("Network polling service stopping");

        // Log session end event
        var sessionDuration = DateTime.Now - _sessionStartTime;
        await _telemetry.LogEventAsync(
            TelemetryCategory.App,
            "SessionEnd",
            $"WireBound monitoring session ended after {sessionDuration.TotalMinutes:F1} minutes",
            value: (long)sessionDuration.TotalSeconds,
            metadata: $"{{\"duration\": \"{sessionDuration}\", \"peakDownload\": {_peakDownloadSpeed}, \"peakUpload\": {_peakUploadSpeed}}}");

        // Final save before shutdown
        try
        {
            var finalStats = _networkMonitor.GetCurrentStats();
            await _persistence.SaveStatsAsync(finalStats);
            _logger.LogInformation("Final stats saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save final stats on shutdown");
        }
    }

    private async Task TrackPeakSpeedsAsync(NetworkStats stats)
    {
        // Track session peak download speed
        if (stats.DownloadSpeedBps > _peakDownloadSpeed)
        {
            var previousPeak = _peakDownloadSpeed;
            _peakDownloadSpeed = stats.DownloadSpeedBps;

            // Log significant speed spikes (more than 50% increase from previous peak and > 10 MB/s)
            if (_peakDownloadSpeed > 10_000_000 && previousPeak > 0 && 
                _peakDownloadSpeed > previousPeak * 1.5)
            {
                await _telemetry.LogEventAsync(
                    TelemetryCategory.Network,
                    "DownloadSpeedSpike",
                    $"New peak download speed: {_peakDownloadSpeed / 1_000_000.0:F1} MB/s",
                    value: _peakDownloadSpeed,
                    adapterId: stats.AdapterId,
                    metadata: $"{{\"previousPeak\": {previousPeak}}}");
            }
        }

        // Track session peak upload speed
        if (stats.UploadSpeedBps > _peakUploadSpeed)
        {
            var previousPeak = _peakUploadSpeed;
            _peakUploadSpeed = stats.UploadSpeedBps;

            // Log significant speed spikes
            if (_peakUploadSpeed > 5_000_000 && previousPeak > 0 && 
                _peakUploadSpeed > previousPeak * 1.5)
            {
                await _telemetry.LogEventAsync(
                    TelemetryCategory.Network,
                    "UploadSpeedSpike",
                    $"New peak upload speed: {_peakUploadSpeed / 1_000_000.0:F1} MB/s",
                    value: _peakUploadSpeed,
                    adapterId: stats.AdapterId,
                    metadata: $"{{\"previousPeak\": {previousPeak}}}");
            }
        }
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WireBound.Maui.Services;

/// <summary>
/// Background service that polls network statistics at regular intervals
/// </summary>
public class NetworkPollingBackgroundService : BackgroundService, INetworkPollingBackgroundService
{
    private readonly INetworkMonitorService _networkMonitor;
    private readonly IDataPersistenceService _persistence;
    private readonly ILogger<NetworkPollingBackgroundService> _logger;
    private volatile int _pollIntervalMs = 1000;
    private volatile int _saveIntervalSeconds = 60;
    private DateTime _lastSaveTime = DateTime.Now;

    public NetworkPollingBackgroundService(
        INetworkMonitorService networkMonitor,
        IDataPersistenceService persistence,
        ILogger<NetworkPollingBackgroundService> logger)
    {
        _networkMonitor = networkMonitor;
        _persistence = persistence;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Network polling service starting");

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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Poll network stats
                _networkMonitor.Poll();

                // Save to database periodically
                if ((DateTime.Now - _lastSaveTime).TotalSeconds >= _saveIntervalSeconds)
                {
                    var stats = _networkMonitor.GetCurrentStats();
                    await _persistence.SaveStatsAsync(stats);
                    _lastSaveTime = DateTime.Now;
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
                await Task.Delay(5000, stoppingToken); // Wait longer on error
            }
        }

        _logger.LogInformation("Network polling service stopping");

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

    /// <inheritdoc />
    public void UpdatePollingInterval(int milliseconds)
    {
        if (milliseconds < 100)
        {
            _logger.LogWarning("Polling interval {Interval}ms is too low, using minimum of 100ms", milliseconds);
            milliseconds = 100;
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
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WireBound.Core.Services;

namespace WireBound.Services;

/// <summary>
/// Background service that polls network statistics at regular intervals
/// </summary>
public sealed class NetworkPollingBackgroundService : BackgroundService, INetworkPollingBackgroundService
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
        var settings = await _persistence.GetSettingsAsync().ConfigureAwait(false);
        _pollIntervalMs = settings.PollingIntervalMs;
        _saveIntervalSeconds = settings.SaveIntervalSeconds;
        _networkMonitor.SetUseIpHelperApi(settings.UseIpHelperApi);

        _logger.LogInformation("Polling interval: {PollIntervalMs}ms, Save interval: {SaveIntervalSeconds}s, IP Helper API: {UseIpHelperApi}",
            _pollIntervalMs, _saveIntervalSeconds, settings.UseIpHelperApi);

        if (!string.IsNullOrEmpty(settings.SelectedAdapterId))
        {
            _networkMonitor.SetAdapter(settings.SelectedAdapterId);
        }

        // Perform an initial save after a short delay to ensure History has data quickly
        bool initialSaveDone = false;
        const int initialSaveDelaySeconds = 5;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Poll network stats
                _networkMonitor.Poll();

                // Initial save after short delay for quick History data availability
                if (!initialSaveDone && (DateTime.Now - _lastSaveTime).TotalSeconds >= initialSaveDelaySeconds)
                {
                    var stats = _networkMonitor.GetCurrentStats();
                    await _persistence.SaveStatsAsync(stats).ConfigureAwait(false);
                    _lastSaveTime = DateTime.Now;
                    initialSaveDone = true;
                    _logger.LogInformation("Initial stats saved for History availability");
                }
                // Regular periodic saves
                else if (initialSaveDone && (DateTime.Now - _lastSaveTime).TotalSeconds >= _saveIntervalSeconds)
                {
                    var stats = _networkMonitor.GetCurrentStats();
                    await _persistence.SaveStatsAsync(stats).ConfigureAwait(false);
                    _lastSaveTime = DateTime.Now;
                }

                await Task.Delay(_pollIntervalMs, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log error but continue running
                _logger.LogError(ex, "Error during network polling");
                await Task.Delay(5000, stoppingToken).ConfigureAwait(false); // Wait longer on error
            }
        }

        _logger.LogInformation("Network polling service stopping");

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
}

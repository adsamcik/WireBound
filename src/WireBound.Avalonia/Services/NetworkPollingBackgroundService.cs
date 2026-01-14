using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

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
    private readonly Stopwatch _saveStopwatch = new();

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

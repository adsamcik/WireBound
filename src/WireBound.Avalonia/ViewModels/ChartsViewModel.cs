using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using WireBound.Avalonia.Helpers;
using WireBound.Core.Helpers;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Charts page (full-screen chart view)
/// </summary>
public sealed partial class ChartsViewModel : ObservableObject, IDisposable
{
    private readonly INetworkMonitorService _networkMonitor;
    private readonly IDataPersistenceService _persistence;
    private readonly ILogger<ChartsViewModel>? _logger;
    private bool _disposed;
    private const int MaxBufferSize = 3600;
    private readonly ObservableCollection<DateTimePoint> _downloadSpeedPoints = [];
    private readonly ObservableCollection<DateTimePoint> _uploadSpeedPoints = [];
    private readonly CircularBuffer<(DateTime Time, long Download, long Upload)> _dataBuffer = new(MaxBufferSize);
    
    private long _peakDownloadBps;
    private long _peakUploadBps;
    private long _totalDownloadBps;
    private long _totalUploadBps;
    private int _sampleCount;

    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _uploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _peakDownloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _peakUploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _averageDownloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _averageUploadSpeed = "0 B/s";

    [ObservableProperty]
    private TimeRangeOption _selectedTimeRange;

    [ObservableProperty]
    private bool _isUpdatesPaused;

    [ObservableProperty]
    private string _pauseStatusText = "";

    public ObservableCollection<TimeRangeOption> TimeRangeOptions { get; } =
    [
        new() { Label = "30s", Seconds = 30, Description = "Last 30 seconds" },
        new() { Label = "1m", Seconds = 60, Description = "Last 1 minute" },
        new() { Label = "5m", Seconds = 300, Description = "Last 5 minutes" },
        new() { Label = "15m", Seconds = 900, Description = "Last 15 minutes" },
        new() { Label = "1h", Seconds = 3600, Description = "Last 1 hour" }
    ];

    public ISeries[] SpeedSeries { get; }

    public Axis[] XAxes { get; }

    public Axis[] YAxes { get; } = ChartSeriesFactory.CreateSpeedYAxes();

    public ChartsViewModel(
        INetworkMonitorService networkMonitor,
        IDataPersistenceService persistence,
        ILogger<ChartsViewModel>? logger = null)
    {
        _networkMonitor = networkMonitor;
        _persistence = persistence;
        _logger = logger;
        networkMonitor.StatsUpdated += OnStatsUpdated;

        _selectedTimeRange = TimeRangeOptions[1];

        // Initialize axes and series using factory
        XAxes = ChartSeriesFactory.CreateTimeXAxes();
        SpeedSeries = ChartSeriesFactory.CreateSpeedLineSeries(_downloadSpeedPoints, _uploadSpeedPoints);

        // Load historical data asynchronously
        _ = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            // Load up to 1 hour of history (max range)
            var since = DateTime.Now.AddHours(-1);
            var history = await _persistence.GetSpeedHistoryAsync(since).ConfigureAwait(false);

            if (history.Count == 0 || _disposed)
                return;

            // Populate buffer and calculate statistics from history
            foreach (var snapshot in history)
            {
                _dataBuffer.Add((snapshot.Timestamp, snapshot.DownloadSpeedBps, snapshot.UploadSpeedBps));

                if (snapshot.DownloadSpeedBps > _peakDownloadBps)
                    _peakDownloadBps = snapshot.DownloadSpeedBps;
                if (snapshot.UploadSpeedBps > _peakUploadBps)
                    _peakUploadBps = snapshot.UploadSpeedBps;

                _totalDownloadBps += snapshot.DownloadSpeedBps;
                _totalUploadBps += snapshot.UploadSpeedBps;
                _sampleCount++;
            }

            // Update UI on dispatcher thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed) return;

                // Update statistics display
                PeakDownloadSpeed = ByteFormatter.FormatSpeed(_peakDownloadBps);
                PeakUploadSpeed = ByteFormatter.FormatSpeed(_peakUploadBps);
                if (_sampleCount > 0)
                {
                    AverageDownloadSpeed = ByteFormatter.FormatSpeed(_totalDownloadBps / _sampleCount);
                    AverageUploadSpeed = ByteFormatter.FormatSpeed(_totalUploadBps / _sampleCount);
                }

                // Apply current time range to show data
                var rangeSeconds = SelectedTimeRange?.Seconds ?? 60;
                var cutoff = DateTime.Now.AddSeconds(-rangeSeconds);
                var relevantData = _dataBuffer.AsEnumerable().Where(d => d.Time >= cutoff).ToList();

                foreach (var d in relevantData)
                {
                    _downloadSpeedPoints.Add(new DateTimePoint(d.Time, d.Download));
                    _uploadSpeedPoints.Add(new DateTimePoint(d.Time, d.Upload));
                }

                // Update axis limits
                if (XAxes.Length > 0 && relevantData.Count > 0)
                {
                    XAxes[0].MinLimit = cutoff.Ticks;
                    XAxes[0].MaxLimit = DateTime.Now.Ticks;
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to load history data for chart - chart will still work with live data");
        }
    }

    private void OnStatsUpdated(object? sender, Core.Models.NetworkStats stats)
    {
        if (_disposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            UpdateUI(stats);
        });
    }

    private void UpdateUI(Core.Models.NetworkStats stats)
    {
        var now = DateTime.Now;

        DownloadSpeed = ByteFormatter.FormatSpeed(stats.DownloadSpeedBps);
        UploadSpeed = ByteFormatter.FormatSpeed(stats.UploadSpeedBps);

        // Update statistics
        if (stats.DownloadSpeedBps > _peakDownloadBps)
        {
            _peakDownloadBps = stats.DownloadSpeedBps;
            PeakDownloadSpeed = ByteFormatter.FormatSpeed(_peakDownloadBps);
        }
        if (stats.UploadSpeedBps > _peakUploadBps)
        {
            _peakUploadBps = stats.UploadSpeedBps;
            PeakUploadSpeed = ByteFormatter.FormatSpeed(_peakUploadBps);
        }

        _totalDownloadBps += stats.DownloadSpeedBps;
        _totalUploadBps += stats.UploadSpeedBps;
        _sampleCount++;

        if (_sampleCount > 0)
        {
            AverageDownloadSpeed = ByteFormatter.FormatSpeed(_totalDownloadBps / _sampleCount);
            AverageUploadSpeed = ByteFormatter.FormatSpeed(_totalUploadBps / _sampleCount);
        }

        // Add to buffer (CircularBuffer auto-evicts oldest when full)
        _dataBuffer.Add((now, stats.DownloadSpeedBps, stats.UploadSpeedBps));

        if (!IsUpdatesPaused)
        {
            var rangeSeconds = SelectedTimeRange?.Seconds ?? 60;
            var cutoff = now.AddSeconds(-rangeSeconds);

            _downloadSpeedPoints.Add(new DateTimePoint(now, stats.DownloadSpeedBps));
            _uploadSpeedPoints.Add(new DateTimePoint(now, stats.UploadSpeedBps));

            // Remove old points from display collections
            while (_downloadSpeedPoints.Count > 0 && _downloadSpeedPoints[0].DateTime < cutoff)
                _downloadSpeedPoints.RemoveAt(0);
            while (_uploadSpeedPoints.Count > 0 && _uploadSpeedPoints[0].DateTime < cutoff)
                _uploadSpeedPoints.RemoveAt(0);

            if (XAxes.Length > 0)
            {
                XAxes[0].MinLimit = cutoff.Ticks;
                XAxes[0].MaxLimit = now.Ticks;
            }
        }
    }

    partial void OnSelectedTimeRangeChanged(TimeRangeOption value)
    {
        if (value == null) return;

        var cutoff = DateTime.Now.AddSeconds(-value.Seconds);
        var relevantData = _dataBuffer.AsEnumerable().Where(d => d.Time >= cutoff).ToList();

        _downloadSpeedPoints.Clear();
        _uploadSpeedPoints.Clear();

        foreach (var d in relevantData)
        {
            _downloadSpeedPoints.Add(new DateTimePoint(d.Time, d.Download));
            _uploadSpeedPoints.Add(new DateTimePoint(d.Time, d.Upload));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _networkMonitor.StatsUpdated -= OnStatsUpdated;
    }
}

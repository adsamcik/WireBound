using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;
using WireBound.Avalonia.Helpers;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Charts page (full-screen chart view)
/// </summary>
public sealed partial class ChartsViewModel : ObservableObject, IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly INetworkMonitorService _networkMonitor;
    private readonly IDataPersistenceService _persistence;
    private readonly INavigationService _navigationService;
    private readonly ISystemMonitorService? _systemMonitorService;
    private readonly ILogger<ChartsViewModel>? _logger;
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _timeRangeCts;
    private bool _disposed;
    private bool _isViewActive;
    private readonly ChartDataManager _chartDataManager = new(maxBufferSize: 3600, maxDisplayPoints: 300);
    private readonly BatchObservableCollection<DateTimePoint> _downloadSpeedPoints = new();
    private readonly BatchObservableCollection<DateTimePoint> _uploadSpeedPoints = new();

    // CPU/Memory history for overlay
    private readonly BatchObservableCollection<DateTimePoint> _cpuHistoryPoints = new();
    private readonly BatchObservableCollection<DateTimePoint> _memoryHistoryPoints = new();

    // Latest-wins coalescing: only one UI post in-flight at a time per event type
    private NetworkStats? _pendingNetworkStats;
    private int _networkUpdateQueued;
    private SystemStats? _pendingSystemStats;
    private int _systemUpdateQueued;

    // Cached formatted values to avoid string allocation when value unchanged
    private long _lastDownloadBps = -1;
    private long _lastUploadBps = -1;
    private long _lastPeakDownloadBps = -1;
    private long _lastPeakUploadBps = -1;
    private long _lastAvgDownloadBps = -1;
    private long _lastAvgUploadBps = -1;

    // Overlay series (created once, added/removed from chart as needed)
    private readonly LineSeries<DateTimePoint> _cpuOverlaySeries;
    private readonly LineSeries<DateTimePoint> _memoryOverlaySeries;

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

    // Layer toggle properties for multi-metric overlays
    [ObservableProperty]
    private bool _showCpuOverlay;

    [ObservableProperty]
    private bool _showMemoryOverlay;

    public ObservableCollection<TimeRangeOption> TimeRangeOptions { get; } =
    [
        new() { Label = "30s", Seconds = 30, Description = "Last 30 seconds" },
        new() { Label = "1m", Seconds = 60, Description = "Last 1 minute" },
        new() { Label = "5m", Seconds = 300, Description = "Last 5 minutes" },
        new() { Label = "15m", Seconds = 900, Description = "Last 15 minutes" },
        new() { Label = "1h", Seconds = 3600, Description = "Last 1 hour" }
    ];

    /// <summary>Completes when async initialization finishes. Exposed for testability.</summary>
    public Task InitializationTask { get; }

    public ObservableCollection<ISeries> SpeedSeries { get; } = [];

    public Axis[] XAxes { get; }

    public Axis[] YAxes { get; } = ChartSeriesFactory.CreateSpeedYAxes();

    /// <summary>
    /// Secondary Y-axis for percentage values (0-100%) when CPU/Memory overlays are shown.
    /// Positioned on the right side of the chart.
    /// </summary>
    public Axis[] SecondaryYAxes { get; } = ChartSeriesFactory.CreatePercentageYAxes();

    public ChartsViewModel(
        IUiDispatcher dispatcher,
        INetworkMonitorService networkMonitor,
        IDataPersistenceService persistence,
        INavigationService navigationService,
        ISystemMonitorService? systemMonitorService = null,
        ILogger<ChartsViewModel>? logger = null)
    {
        _dispatcher = dispatcher;
        _networkMonitor = networkMonitor;
        _persistence = persistence;
        _navigationService = navigationService;
        _systemMonitorService = systemMonitorService;
        _logger = logger;
        _isViewActive = navigationService.CurrentView == Routes.Charts;
        networkMonitor.StatsUpdated += OnStatsUpdated;
        navigationService.NavigationChanged += OnNavigationChanged;

        _selectedTimeRange = TimeRangeOptions[1];

        // Initialize axes and series using factory
        XAxes = ChartSeriesFactory.CreateTimeXAxes();

        // Add base network speed series
        var baseSeries = ChartSeriesFactory.CreateSpeedLineSeries(_downloadSpeedPoints, _uploadSpeedPoints);
        foreach (var series in baseSeries)
            SpeedSeries.Add(series);

        // Create overlay series (dashed lines for CPU/Memory)
        _cpuOverlaySeries = ChartSeriesFactory.CreateOverlayLineSeries(
            "CPU %", _cpuHistoryPoints, ChartColors.CpuColor, useDashedLine: true);
        _memoryOverlaySeries = ChartSeriesFactory.CreateOverlayLineSeries(
            "Memory %", _memoryHistoryPoints, ChartColors.MemoryColor, useDashedLine: true);

        // Subscribe to system stats if service is available
        if (_systemMonitorService != null)
        {
            _systemMonitorService.StatsUpdated += OnSystemStatsUpdated;
        }

        // Load historical data asynchronously
        InitializationTask = LoadHistoryAsync();
    }

    private async Task LoadHistoryAsync()
    {
        try
        {
            var token = _cts.Token;

            // Load up to 1 hour of history (max range)
            var since = DateTime.Now.AddHours(-1);
            var history = await _persistence.GetSpeedHistoryAsync(since).ConfigureAwait(false);

            if (history.Count == 0 || token.IsCancellationRequested)
                return;

            // Load history into ChartDataManager (populates buffer and updates statistics)
            _chartDataManager.LoadHistory(history.Select(s => (s.Timestamp, s.DownloadSpeedBps, s.UploadSpeedBps)));

            // Run LTTB downsampling on background thread
            var rangeSeconds = SelectedTimeRange?.Seconds ?? 60;
            var (downloadPoints, uploadPoints) = await _chartDataManager.GetDisplayDataAsync(rangeSeconds);

            // Update UI on dispatcher thread — only lightweight collection swap
            await _dispatcher.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;

                // Update statistics display from ChartDataManager
                PeakDownloadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.PeakDownloadBps);
                PeakUploadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.PeakUploadBps);
                AverageDownloadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.AverageDownloadBps);
                AverageUploadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.AverageUploadBps);

                _downloadSpeedPoints.AddRange(downloadPoints);
                _uploadSpeedPoints.AddRange(uploadPoints);

                // Update axis limits
                if (XAxes.Length > 0 && downloadPoints.Count > 0)
                {
                    var cutoff = DateTime.Now.AddSeconds(-rangeSeconds);
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

    private void OnNavigationChanged(string route)
    {
        var wasActive = _isViewActive;
        _isViewActive = route == Routes.Charts;

        if (_isViewActive && !wasActive)
        {
            // Refresh chart display from buffer when becoming visible
            _dispatcher.Post(RefreshChartFromBuffer);
        }
    }

    private async void RefreshChartFromBuffer()
    {
        try
        {
            var rangeSeconds = SelectedTimeRange?.Seconds ?? 60;
            var (downloadPoints, uploadPoints) = await _chartDataManager.GetDisplayDataAsync(rangeSeconds);

            _downloadSpeedPoints.ReplaceAll(downloadPoints);
            _uploadSpeedPoints.ReplaceAll(uploadPoints);

            // Update statistics
            PeakDownloadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.PeakDownloadBps);
            PeakUploadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.PeakUploadBps);
            AverageDownloadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.AverageDownloadBps);
            AverageUploadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.AverageUploadBps);

            // Update axis limits
            if (XAxes.Length > 0 && downloadPoints.Count > 0)
            {
                var cutoff = DateTime.Now.AddSeconds(-rangeSeconds);
                XAxes[0].MinLimit = cutoff.Ticks;
                XAxes[0].MaxLimit = DateTime.Now.Ticks;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to refresh chart from buffer");
        }
    }

    private void OnStatsUpdated(object? sender, Core.Models.NetworkStats stats)
    {
        if (_disposed) return;

        Volatile.Write(ref _pendingNetworkStats, stats);
        if (Interlocked.Exchange(ref _networkUpdateQueued, 1) == 1) return;

        _dispatcher.Post(() =>
        {
            if (_disposed) return;
            var pending = _pendingNetworkStats;
            Interlocked.Exchange(ref _networkUpdateQueued, 0);
            if (pending != null) UpdateUI(pending);
        }, UiDispatcherPriority.Background);
    }

    private void UpdateUI(Core.Models.NetworkStats stats)
    {
        var now = DateTime.Now;

        // Always buffer data (needed for chart history even when not visible)
        _chartDataManager.AddDataPoint(now, stats.DownloadSpeedBps, stats.UploadSpeedBps);

        // Skip all UI updates when view is not active
        if (!_isViewActive) return;

        if (_lastDownloadBps != stats.DownloadSpeedBps)
        {
            _lastDownloadBps = stats.DownloadSpeedBps;
            DownloadSpeed = ByteFormatter.FormatSpeed(stats.DownloadSpeedBps);
        }
        if (_lastUploadBps != stats.UploadSpeedBps)
        {
            _lastUploadBps = stats.UploadSpeedBps;
            UploadSpeed = ByteFormatter.FormatSpeed(stats.UploadSpeedBps);
        }

        // Update statistics display (only when values change)
        var peakDown = _chartDataManager.PeakDownloadBps;
        var peakUp = _chartDataManager.PeakUploadBps;
        var avgDown = _chartDataManager.AverageDownloadBps;
        var avgUp = _chartDataManager.AverageUploadBps;

        if (_lastPeakDownloadBps != peakDown)
        {
            _lastPeakDownloadBps = peakDown;
            PeakDownloadSpeed = ByteFormatter.FormatSpeed(peakDown);
        }
        if (_lastPeakUploadBps != peakUp)
        {
            _lastPeakUploadBps = peakUp;
            PeakUploadSpeed = ByteFormatter.FormatSpeed(peakUp);
        }
        if (_lastAvgDownloadBps != avgDown)
        {
            _lastAvgDownloadBps = avgDown;
            AverageDownloadSpeed = ByteFormatter.FormatSpeed(avgDown);
        }
        if (_lastAvgUploadBps != avgUp)
        {
            _lastAvgUploadBps = avgUp;
            AverageUploadSpeed = ByteFormatter.FormatSpeed(avgUp);
        }

        if (!IsUpdatesPaused)
        {
            var rangeSeconds = SelectedTimeRange?.Seconds ?? 60;
            var cutoff = now.AddSeconds(-rangeSeconds);

            _downloadSpeedPoints.Add(new DateTimePoint(now, stats.DownloadSpeedBps));
            _uploadSpeedPoints.Add(new DateTimePoint(now, stats.UploadSpeedBps));

            // Remove old points from display collections
            ChartCollectionHelper.TrimBeforeCutoff(_downloadSpeedPoints, cutoff);
            ChartCollectionHelper.TrimBeforeCutoff(_uploadSpeedPoints, cutoff);

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

        // Cancel any in-flight time range update to prevent stale data from overwriting
        _timeRangeCts?.Cancel();
        _timeRangeCts = new CancellationTokenSource();
        _ = UpdateTimeRangeAsync(value.Seconds, _timeRangeCts.Token);
    }

    private async Task UpdateTimeRangeAsync(int rangeSeconds, CancellationToken token)
    {
        var (downloadPoints, uploadPoints) = await _chartDataManager.GetDisplayDataAsync(rangeSeconds);

        if (token.IsCancellationRequested) return;

        _downloadSpeedPoints.ReplaceAll(downloadPoints);
        _uploadSpeedPoints.ReplaceAll(uploadPoints);
    }

    /// <summary>
    /// Handles system stats updates for CPU/Memory overlays
    /// </summary>
    private void OnSystemStatsUpdated(object? sender, Core.Models.SystemStats stats)
    {
        if (_disposed || !_isViewActive || (!ShowCpuOverlay && !ShowMemoryOverlay)) return;

        Volatile.Write(ref _pendingSystemStats, stats);
        if (Interlocked.Exchange(ref _systemUpdateQueued, 1) == 1) return;

        _dispatcher.Post(() =>
        {
            if (_disposed) return;
            var pending = _pendingSystemStats;
            Interlocked.Exchange(ref _systemUpdateQueued, 0);
            if (pending != null) UpdateSystemOverlays(pending);
        }, UiDispatcherPriority.Background);
    }

    private void UpdateSystemOverlays(Core.Models.SystemStats stats)
    {
        var now = DateTime.Now;
        var rangeSeconds = SelectedTimeRange?.Seconds ?? 60;
        var cutoff = now.AddSeconds(-rangeSeconds);

        if (ShowCpuOverlay)
        {
            _cpuHistoryPoints.Add(new DateTimePoint(now, stats.Cpu.UsagePercent));
            ChartCollectionHelper.TrimBeforeCutoff(_cpuHistoryPoints, cutoff);
        }

        if (ShowMemoryOverlay)
        {
            _memoryHistoryPoints.Add(new DateTimePoint(now, stats.Memory.UsagePercent));
            ChartCollectionHelper.TrimBeforeCutoff(_memoryHistoryPoints, cutoff);
        }
    }

    /// <summary>
    /// Called when ShowCpuOverlay property changes - adds/removes CPU series from chart
    /// </summary>
    partial void OnShowCpuOverlayChanged(bool value)
    {
        if (value)
        {
            if (!SpeedSeries.Contains(_cpuOverlaySeries))
                SpeedSeries.Add(_cpuOverlaySeries);
        }
        else
        {
            SpeedSeries.Remove(_cpuOverlaySeries);
            _cpuHistoryPoints.Clear();
        }
    }

    /// <summary>
    /// Called when ShowMemoryOverlay property changes - adds/removes Memory series from chart
    /// </summary>
    partial void OnShowMemoryOverlayChanged(bool value)
    {
        if (value)
        {
            if (!SpeedSeries.Contains(_memoryOverlaySeries))
                SpeedSeries.Add(_memoryOverlaySeries);
        }
        else
        {
            SpeedSeries.Remove(_memoryOverlaySeries);
            _memoryHistoryPoints.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
        _timeRangeCts?.Cancel();
        _timeRangeCts?.Dispose();

        _networkMonitor.StatsUpdated -= OnStatsUpdated;
        _navigationService.NavigationChanged -= OnNavigationChanged;
        if (_systemMonitorService != null)
        {
            _systemMonitorService.StatsUpdated -= OnSystemStatsUpdated;
        }
    }
}

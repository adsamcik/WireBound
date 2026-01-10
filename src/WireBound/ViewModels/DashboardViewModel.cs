using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using System.Collections.ObjectModel;
using WireBound.Helpers;
using WireBound.Models;
using WireBound.Services;

namespace WireBound.ViewModels;

/// <summary>
/// Represents a time range option for the chart display
/// </summary>
public class TimeRangeOption
{
    public string Label { get; init; } = "";
    public TimeSpan Duration { get; init; }
    public int MaxDataPoints { get; init; }
    public string TimeFormat { get; init; } = "HH:mm:ss";
    public TimeSpan MinStep { get; init; }
}

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly INetworkMonitorService _networkMonitor;
    private readonly IDataPersistenceService _persistence;
    private readonly IProcessNetworkService? _processNetworkService;
    private bool _disposed;
    private readonly ObservableCollection<DateTimePoint> _downloadSpeedPoints = new();
    private readonly ObservableCollection<DateTimePoint> _uploadSpeedPoints = new();
    
    // Data buffer to store more points than currently displayed (for time range switching)
    private readonly List<(DateTime Timestamp, long DownloadSpeed, long UploadSpeed)> _dataBuffer = new();
    private const int MaxBufferSize = 3600; // Store up to 1 hour of data (1 point per second)

    /// <summary>
    /// Available time range options for the chart
    /// </summary>
    public static TimeRangeOption[] TimeRangeOptions { get; } =
    [
        new() { Label = "30s", Duration = TimeSpan.FromSeconds(30), MaxDataPoints = 30, TimeFormat = "HH:mm:ss", MinStep = TimeSpan.FromSeconds(1) },
        new() { Label = "1m", Duration = TimeSpan.FromMinutes(1), MaxDataPoints = 60, TimeFormat = "HH:mm:ss", MinStep = TimeSpan.FromSeconds(1) },
        new() { Label = "5m", Duration = TimeSpan.FromMinutes(5), MaxDataPoints = 300, TimeFormat = "HH:mm:ss", MinStep = TimeSpan.FromSeconds(5) },
        new() { Label = "15m", Duration = TimeSpan.FromMinutes(15), MaxDataPoints = 900, TimeFormat = "HH:mm", MinStep = TimeSpan.FromSeconds(15) },
        new() { Label = "1h", Duration = TimeSpan.FromHours(1), MaxDataPoints = 3600, TimeFormat = "HH:mm", MinStep = TimeSpan.FromMinutes(1) },
    ];

    [ObservableProperty]
    private TimeRangeOption _selectedTimeRange = TimeRangeOptions[1]; // Default to 1 minute

    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _uploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _sessionDownload = "0 B";

    [ObservableProperty]
    private string _sessionUpload = "0 B";

    [ObservableProperty]
    private string _peakDownloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _peakUploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _averageDownloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _averageUploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _selectedAdapterName = "All Adapters";

    [ObservableProperty]
    private ObservableCollection<NetworkAdapter> _adapters = new();

    [ObservableProperty]
    private NetworkAdapter? _selectedAdapter;

    [ObservableProperty]
    private bool _isAutoScaleEnabled = true;

    [ObservableProperty]
    private bool _isPerAppTrackingEnabled = false;

    [ObservableProperty]
    private ObservableCollection<ProcessNetworkStats> _topApps = new();

    public ISeries[] SpeedSeries { get; }

    [ObservableProperty]
    private Axis[] _xAxes;

    [ObservableProperty]
    private Axis[] _yAxes;

    private void UpdateAxesForTimeRange(TimeRangeOption timeRange)
    {
        XAxes =
        [
            new DateTimeAxis(timeRange.MinStep, date => date.ToString(timeRange.TimeFormat))
            {
                Name = "Time",
                NamePaint = new SolidColorPaint(SKColors.Gray),
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0),
                MinStep = timeRange.MinStep.Ticks
            }
        ];

        YAxes =
        [
            new Axis
            {
                Name = "Speed",
                NamePaint = new SolidColorPaint(SKColors.Gray),
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                MinLimit = 0,
                Labeler = value => ByteFormatter.FormatSpeed((long)value)
            }
        ];
    }

    public DashboardViewModel(
        INetworkMonitorService networkMonitor,
        IDataPersistenceService persistence,
        IServiceProvider serviceProvider)
    {
        _networkMonitor = networkMonitor;
        _persistence = persistence;
        _networkMonitor.StatsUpdated += OnStatsUpdated;

        // Try to get optional process network service
        _processNetworkService = serviceProvider.GetService<IProcessNetworkService>();

        // Initialize axes with default time range
        _xAxes = [];
        _yAxes = [];
        UpdateAxesForTimeRange(SelectedTimeRange);

        // Initialize chart series
        SpeedSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Name = "Download",
                Values = _downloadSpeedPoints,
                Fill = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(50)),
                Stroke = new SolidColorPaint(SKColors.DodgerBlue, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.3,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0),
                YToolTipLabelFormatter = point => $"⬇ {ByteFormatter.FormatSpeed((long)(point.Model?.Value ?? 0))}"
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Upload",
                Values = _uploadSpeedPoints,
                Fill = new SolidColorPaint(SKColors.LimeGreen.WithAlpha(50)),
                Stroke = new SolidColorPaint(SKColors.LimeGreen, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.3,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0),
                YToolTipLabelFormatter = point => $"⬆ {ByteFormatter.FormatSpeed((long)(point.Model?.Value ?? 0))}"
            }
        ];

        LoadAdapters();

        // Initialize per-app tracking
        _ = InitializePerAppTrackingAsync();
    }

    private async Task InitializePerAppTrackingAsync()
    {
        var settings = await _persistence.GetSettingsAsync();
        IsPerAppTrackingEnabled = settings.IsPerAppTrackingEnabled;

        if (_processNetworkService != null && IsPerAppTrackingEnabled)
        {
            _processNetworkService.StatsUpdated += OnProcessStatsUpdated;
        }
    }

    private void OnProcessStatsUpdated(object? sender, ProcessStatsUpdatedEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            var top5 = e.Stats
                .OrderByDescending(p => p.TotalSpeedBps)
                .Take(5)
                .ToList();

            TopApps.Clear();
            foreach (var app in top5)
            {
                TopApps.Add(app);
            }
        });
    }

    partial void OnSelectedTimeRangeChanged(TimeRangeOption value)
    {
        UpdateAxesForTimeRange(value);
        RefreshChartFromBuffer();
    }

    /// <summary>
    /// Select a time range by its label
    /// </summary>
    [RelayCommand]
    private void SelectTimeRange(string label)
    {
        var range = TimeRangeOptions.FirstOrDefault(r => r.Label == label);
        if (range != null)
        {
            SelectedTimeRange = range;
        }
    }

    /// <summary>
    /// Reset chart zoom to default view
    /// </summary>
    [RelayCommand]
    private void ResetZoom()
    {
        // Re-initialize axes to reset zoom
        UpdateAxesForTimeRange(SelectedTimeRange);
    }

    /// <summary>
    /// Toggle auto-scale on Y axis
    /// </summary>
    [RelayCommand]
    private void ToggleAutoScale()
    {
        IsAutoScaleEnabled = !IsAutoScaleEnabled;
        UpdateYAxisScale();
    }

    private void UpdateYAxisScale()
    {
        if (YAxes.Length > 0 && YAxes[0] is Axis yAxis)
        {
            if (IsAutoScaleEnabled)
            {
                yAxis.MinLimit = 0;
                yAxis.MaxLimit = null; // Auto
            }
            else
            {
                // Lock to current max + 20% headroom
                var currentMax = _downloadSpeedPoints.Concat(_uploadSpeedPoints)
                    .Where(p => p.Value.HasValue)
                    .Select(p => p.Value!.Value)
                    .DefaultIfEmpty(0)
                    .Max();
                yAxis.MinLimit = 0;
                yAxis.MaxLimit = currentMax * 1.2;
            }
        }
    }

    private void LoadAdapters()
    {
        Adapters.Clear();
        
        // Add "All Adapters" option
        Adapters.Add(new NetworkAdapter { Id = "", Name = "All Adapters" });
        
        foreach (var adapter in _networkMonitor.GetAdapters())
        {
            Adapters.Add(adapter);
        }
    }

    partial void OnSelectedAdapterChanged(NetworkAdapter? value)
    {
        if (value != null)
        {
            _networkMonitor.SetAdapter(value.Id);
            SelectedAdapterName = value.Name;
            _networkMonitor.ResetSession();
            ClearChart();
        }
    }

    private void OnStatsUpdated(object? sender, NetworkStats stats)
    {
        // Update on UI thread
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            DownloadSpeed = stats.DownloadSpeedFormatted;
            UploadSpeed = stats.UploadSpeedFormatted;
            SessionDownload = stats.SessionReceivedFormatted;
            SessionUpload = stats.SessionSentFormatted;

            var now = stats.Timestamp;

            // Add to buffer for historical data
            _dataBuffer.Add((now, stats.DownloadSpeedBps, stats.UploadSpeedBps));

            // Trim buffer to max size
            while (_dataBuffer.Count > MaxBufferSize)
            {
                _dataBuffer.RemoveAt(0);
            }

            // Update chart points
            _downloadSpeedPoints.Add(new DateTimePoint(now, stats.DownloadSpeedBps));
            _uploadSpeedPoints.Add(new DateTimePoint(now, stats.UploadSpeedBps));

            // Keep only points within selected time range
            var cutoff = now - SelectedTimeRange.Duration;
            while (_downloadSpeedPoints.Count > 0 && _downloadSpeedPoints[0].DateTime < cutoff)
            {
                _downloadSpeedPoints.RemoveAt(0);
                _uploadSpeedPoints.RemoveAt(0);
            }

            // Update statistics
            UpdateChartStatistics();
        });
    }

    /// <summary>
    /// Refresh chart display from the data buffer based on current time range
    /// </summary>
    private void RefreshChartFromBuffer()
    {
        _downloadSpeedPoints.Clear();
        _uploadSpeedPoints.Clear();

        if (_dataBuffer.Count == 0) return;

        var cutoff = DateTime.Now - SelectedTimeRange.Duration;
        var relevantData = _dataBuffer.Where(d => d.Timestamp >= cutoff).ToList();

        foreach (var (timestamp, download, upload) in relevantData)
        {
            _downloadSpeedPoints.Add(new DateTimePoint(timestamp, download));
            _uploadSpeedPoints.Add(new DateTimePoint(timestamp, upload));
        }

        UpdateChartStatistics();
    }

    /// <summary>
    /// Calculate and update peak/average statistics for visible chart data
    /// </summary>
    private void UpdateChartStatistics()
    {
        if (_downloadSpeedPoints.Count == 0)
        {
            PeakDownloadSpeed = "0 B/s";
            PeakUploadSpeed = "0 B/s";
            AverageDownloadSpeed = "0 B/s";
            AverageUploadSpeed = "0 B/s";
            return;
        }

        var downloadValues = _downloadSpeedPoints
            .Where(p => p.Value.HasValue)
            .Select(p => p.Value!.Value)
            .ToList();

        var uploadValues = _uploadSpeedPoints
            .Where(p => p.Value.HasValue)
            .Select(p => p.Value!.Value)
            .ToList();

        if (downloadValues.Count > 0)
        {
            PeakDownloadSpeed = ByteFormatter.FormatSpeed((long)downloadValues.Max());
            AverageDownloadSpeed = ByteFormatter.FormatSpeed((long)downloadValues.Average());
        }

        if (uploadValues.Count > 0)
        {
            PeakUploadSpeed = ByteFormatter.FormatSpeed((long)uploadValues.Max());
            AverageUploadSpeed = ByteFormatter.FormatSpeed((long)uploadValues.Average());
        }
    }

    private void ClearChart()
    {
        _downloadSpeedPoints.Clear();
        _uploadSpeedPoints.Clear();
        _dataBuffer.Clear();
        PeakDownloadSpeed = "0 B/s";
        PeakUploadSpeed = "0 B/s";
        AverageDownloadSpeed = "0 B/s";
        AverageUploadSpeed = "0 B/s";
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _networkMonitor.StatsUpdated -= OnStatsUpdated;

            if (_processNetworkService != null)
            {
                _processNetworkService.StatsUpdated -= OnProcessStatsUpdated;
            }
        }

        _disposed = true;
    }
}

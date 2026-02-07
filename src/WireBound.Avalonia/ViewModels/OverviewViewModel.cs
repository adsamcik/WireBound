using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Time range options for the overview chart
/// </summary>
public enum TimeRange
{
    OneMinute,
    FiveMinutes,
    FifteenMinutes,
    OneHour
}

/// <summary>
/// Unified ViewModel combining network monitoring and system metrics
/// for the overview dashboard experience.
/// </summary>
public sealed partial class OverviewViewModel : ObservableObject, IDisposable
{
    private readonly INetworkMonitorService _networkMonitor;
    private readonly ISystemMonitorService _systemMonitor;
    private readonly IDataPersistenceService? _dataPersistence;
    private readonly ILogger<OverviewViewModel>? _logger;
    private bool _disposed;

    // Chart data collections
    private readonly ObservableCollection<DateTimePoint> _downloadSpeedPoints = [];
    private readonly ObservableCollection<DateTimePoint> _uploadSpeedPoints = [];
    private readonly ObservableCollection<DateTimePoint> _cpuOverlayPoints = [];
    private readonly ObservableCollection<DateTimePoint> _memoryOverlayPoints = [];

    private readonly ChartDataManager _chartDataManager = new(maxBufferSize: 3600, maxDisplayPoints: 300);

    // Trend tracking using shared calculator (arrows style for overview)
    private readonly TrendIndicatorCalculator _downloadTrendCalculator = new(iconStyle: TrendIconStyle.Arrows);
    private readonly TrendIndicatorCalculator _uploadTrendCalculator = new(iconStyle: TrendIconStyle.Arrows);

    // Today's stored bytes (from database at startup)
    private long _todayStoredReceived;
    private long _todayStoredSent;

    // Chart history limits
    private const int MaxHistoryPoints = 300;

    #region Network Properties

    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _uploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _peakDownloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _peakUploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _todayDownload = "0 B";

    [ObservableProperty]
    private string _todayUpload = "0 B";

    [ObservableProperty]
    private string _sessionDownload = "0 B";

    [ObservableProperty]
    private string _sessionUpload = "0 B";

    [ObservableProperty]
    private string _downloadTrendIcon = "";

    [ObservableProperty]
    private string _downloadTrendText = "stable";

    [ObservableProperty]
    private string _uploadTrendIcon = "";

    [ObservableProperty]
    private string _uploadTrendText = "stable";

    [ObservableProperty]
    private ObservableCollection<AdapterDisplayItem> _adapters = [];

    [ObservableProperty]
    private AdapterDisplayItem? _selectedAdapter;

    [ObservableProperty]
    private bool _showAdvancedAdapters;

    #endregion

    #region System Properties

    [ObservableProperty]
    private double _cpuPercent;

    [ObservableProperty]
    private double _memoryPercent;

    [ObservableProperty]
    private string _cpuUsageFormatted = "0%";

    [ObservableProperty]
    private string _memoryUsageFormatted = "0%";

    #endregion

    #region Chart Properties

    [ObservableProperty]
    private bool _showCpuOverlay;

    [ObservableProperty]
    private bool _showMemoryOverlay;

    [ObservableProperty]
    private TimeRange _selectedTimeRange = TimeRange.OneMinute;

    /// <summary>
    /// Main chart series (download/upload with optional CPU/Memory overlays)
    /// </summary>
    public ISeries[] ChartSeries { get; private set; }

    /// <summary>
    /// X-axis configuration for the chart
    /// </summary>
    public Axis[] ChartXAxes { get; }

    /// <summary>
    /// Y-axis configuration for the chart (primary: speed, secondary: percentage)
    /// </summary>
    public Axis[] ChartYAxes { get; }

    /// <summary>
    /// Secondary Y-axis for percentage overlays (CPU/Memory)
    /// </summary>
    public Axis[] ChartSecondaryYAxes { get; }

    /// <summary>
    /// Time range options for display in UI
    /// </summary>
    public static IReadOnlyList<TimeRangeDisplayItem> TimeRangeOptions { get; } =
    [
        new(TimeRange.OneMinute, "1m", "Last 1 minute", 60),
        new(TimeRange.FiveMinutes, "5m", "Last 5 minutes", 300),
        new(TimeRange.FifteenMinutes, "15m", "Last 15 minutes", 900),
        new(TimeRange.OneHour, "1h", "Last 1 hour", 3600)
    ];

    #endregion

    public OverviewViewModel(
        INetworkMonitorService networkMonitor,
        ISystemMonitorService systemMonitor,
        IDataPersistenceService? dataPersistence = null,
        ILogger<OverviewViewModel>? logger = null)
    {
        _networkMonitor = networkMonitor;
        _systemMonitor = systemMonitor;
        _dataPersistence = dataPersistence;
        _logger = logger;

        // Initialize chart axes
        ChartXAxes = ChartSeriesFactory.CreateTimeXAxes();
        ChartYAxes = ChartSeriesFactory.CreateSpeedYAxes();
        ChartSecondaryYAxes = CreatePercentageYAxes();

        // Initialize chart series
        ChartSeries = CreateChartSeries();

        // Subscribe to network stats updates
        _networkMonitor.StatsUpdated += OnNetworkStatsUpdated;

        // Subscribe to system stats updates
        _systemMonitor.StatsUpdated += OnSystemStatsUpdated;

        // Load adapters
        LoadAdapters();

        // Load today's stored usage from database
        _ = LoadTodayUsageAsync();

        // Get initial system stats
        var initialSystemStats = _systemMonitor.GetCurrentStats();
        UpdateSystemProperties(initialSystemStats);
    }

    #region Event Handlers

    private void OnNetworkStatsUpdated(object? sender, NetworkStats stats)
    {
        if (_disposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            UpdateNetworkProperties(stats);
        });
    }

    private void OnSystemStatsUpdated(object? sender, SystemStats stats)
    {
        if (_disposed) return;

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed) return;
            UpdateSystemProperties(stats);
        });
    }

    #endregion

    #region Property Updates

    private void UpdateNetworkProperties(NetworkStats stats)
    {
        var now = DateTime.Now;

        // Update speed displays
        DownloadSpeed = ByteFormatter.FormatSpeed(stats.DownloadSpeedBps);
        UploadSpeed = ByteFormatter.FormatSpeed(stats.UploadSpeedBps);

        // Update session totals
        SessionDownload = ByteFormatter.FormatBytes(stats.SessionBytesReceived);
        SessionUpload = ByteFormatter.FormatBytes(stats.SessionBytesSent);

        // Calculate today's totals (stored from previous sessions + current session)
        var todayReceivedTotal = _todayStoredReceived + stats.SessionBytesReceived;
        var todaySentTotal = _todayStoredSent + stats.SessionBytesSent;
        TodayDownload = ByteFormatter.FormatBytes(todayReceivedTotal);
        TodayUpload = ByteFormatter.FormatBytes(todaySentTotal);

        // Update trend indicators
        UpdateTrendIndicators(stats.DownloadSpeedBps, stats.UploadSpeedBps);

        // Update chart data
        _chartDataManager.AddDataPoint(now, stats.DownloadSpeedBps, stats.UploadSpeedBps);
        UpdateChart(now, stats.DownloadSpeedBps, stats.UploadSpeedBps);

        // Update statistics
        UpdatePeakSpeeds();
    }

    private void UpdateSystemProperties(SystemStats stats)
    {
        // Update CPU properties
        CpuPercent = stats.Cpu.UsagePercent;
        CpuUsageFormatted = $"{stats.Cpu.UsagePercent:F0}%";

        // Update Memory properties
        MemoryPercent = stats.Memory.UsagePercent;
        MemoryUsageFormatted = $"{stats.Memory.UsagePercent:F0}%";

        // Update overlay chart data if enabled
        if (ShowCpuOverlay || ShowMemoryOverlay)
        {
            var timestamp = stats.Timestamp;

            if (ShowCpuOverlay)
            {
                AddChartPoint(_cpuOverlayPoints, timestamp, stats.Cpu.UsagePercent);
            }

            if (ShowMemoryOverlay)
            {
                AddChartPoint(_memoryOverlayPoints, timestamp, stats.Memory.UsagePercent);
            }
        }
    }

    private void UpdateTrendIndicators(long downloadBps, long uploadBps)
    {
        var downloadTrend = _downloadTrendCalculator.Update(downloadBps);
        DownloadTrendIcon = downloadTrend.Icon;
        DownloadTrendText = downloadTrend.Direction.ToString().ToLowerInvariant();

        var uploadTrend = _uploadTrendCalculator.Update(uploadBps);
        UploadTrendIcon = uploadTrend.Icon;
        UploadTrendText = uploadTrend.Direction.ToString().ToLowerInvariant();
    }

    private void UpdatePeakSpeeds()
    {
        PeakDownloadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.PeakDownloadBps);
        PeakUploadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.PeakUploadBps);
    }

    #endregion

    #region Chart Management

    private void UpdateChart(DateTime now, long downloadBps, long uploadBps)
    {
        // Add points to chart
        AddChartPoint(_downloadSpeedPoints, now, downloadBps);
        AddChartPoint(_uploadSpeedPoints, now, uploadBps);

        // Update time range on X-axis
        var rangeSeconds = GetTimeRangeSeconds(SelectedTimeRange);
        if (ChartXAxes.Length > 0)
        {
            ChartXAxes[0].MinLimit = now.AddSeconds(-rangeSeconds).Ticks;
            ChartXAxes[0].MaxLimit = now.Ticks;
        }
    }

    private void AddChartPoint(ObservableCollection<DateTimePoint> points, DateTime timestamp, double value)
    {
        points.Add(new DateTimePoint(timestamp, value));

        // Keep only the last MaxHistoryPoints using batch removal
        TrimCollectionToMaxCount(points, MaxHistoryPoints);
    }

    /// <summary>
    /// Efficiently removes excess items from the beginning using batch removal.
    /// This is O(n) compared to O(n²) for repeated RemoveAt(0) calls.
    /// </summary>
    private static void TrimCollectionToMaxCount(ObservableCollection<DateTimePoint> points, int maxCount)
    {
        var removeCount = points.Count - maxCount;
        if (removeCount <= 0)
            return;

        // Copy items we want to keep to an array 
        var keepCount = points.Count - removeCount;
        var pointsToKeep = new DateTimePoint[keepCount];
        for (var i = 0; i < keepCount; i++)
            pointsToKeep[i] = points[removeCount + i];

        // Clear and re-add (triggers fewer UI updates than multiple RemoveAt)
        points.Clear();
        foreach (var point in pointsToKeep)
            points.Add(point);
    }

    private ISeries[] CreateChartSeries()
    {
        var series = new List<ISeries>();

        // Download series
        series.Add(new LineSeries<DateTimePoint>
        {
            Name = "Download",
            Values = _downloadSpeedPoints,
            Fill = new LinearGradientPaint(
                [ChartColors.DownloadAccentColor.WithAlpha(100), ChartColors.DownloadAccentColor.WithAlpha(0)],
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1)
            ),
            Stroke = new SolidColorPaint(ChartColors.DownloadAccentColor, 2),
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 1,
            AnimationsSpeed = TimeSpan.Zero,
            ScalesYAt = 0 // Primary Y-axis
        });

        // Upload series
        series.Add(new LineSeries<DateTimePoint>
        {
            Name = "Upload",
            Values = _uploadSpeedPoints,
            Fill = new LinearGradientPaint(
                [ChartColors.UploadAccentColor.WithAlpha(100), ChartColors.UploadAccentColor.WithAlpha(0)],
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1)
            ),
            Stroke = new SolidColorPaint(ChartColors.UploadAccentColor, 2),
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 1,
            AnimationsSpeed = TimeSpan.Zero,
            ScalesYAt = 0 // Primary Y-axis
        });

        return [.. series];
    }

    private void RebuildChartSeries()
    {
        var series = new List<ISeries>();

        // Always include download/upload series
        series.Add(new LineSeries<DateTimePoint>
        {
            Name = "Download",
            Values = _downloadSpeedPoints,
            Fill = new LinearGradientPaint(
                [ChartColors.DownloadAccentColor.WithAlpha(100), ChartColors.DownloadAccentColor.WithAlpha(0)],
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1)
            ),
            Stroke = new SolidColorPaint(ChartColors.DownloadAccentColor, 2),
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 1,
            AnimationsSpeed = TimeSpan.Zero,
            ScalesYAt = 0
        });

        series.Add(new LineSeries<DateTimePoint>
        {
            Name = "Upload",
            Values = _uploadSpeedPoints,
            Fill = new LinearGradientPaint(
                [ChartColors.UploadAccentColor.WithAlpha(100), ChartColors.UploadAccentColor.WithAlpha(0)],
                new SKPoint(0.5f, 0),
                new SKPoint(0.5f, 1)
            ),
            Stroke = new SolidColorPaint(ChartColors.UploadAccentColor, 2),
            GeometryFill = null,
            GeometryStroke = null,
            LineSmoothness = 1,
            AnimationsSpeed = TimeSpan.Zero,
            ScalesYAt = 0
        });

        // Add CPU overlay if enabled
        if (ShowCpuOverlay)
        {
            series.Add(new LineSeries<DateTimePoint>
            {
                Name = "CPU",
                Values = _cpuOverlayPoints,
                Fill = null,
                Stroke = new SolidColorPaint(SKColors.DodgerBlue.WithAlpha(180), 1.5f),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5,
                AnimationsSpeed = TimeSpan.Zero,
                ScalesYAt = 1 // Secondary Y-axis (percentage)
            });
        }

        // Add Memory overlay if enabled
        if (ShowMemoryOverlay)
        {
            series.Add(new LineSeries<DateTimePoint>
            {
                Name = "Memory",
                Values = _memoryOverlayPoints,
                Fill = null,
                Stroke = new SolidColorPaint(SKColors.MediumPurple.WithAlpha(180), 1.5f),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5,
                AnimationsSpeed = TimeSpan.Zero,
                ScalesYAt = 1 // Secondary Y-axis (percentage)
            });
        }

        ChartSeries = [.. series];
        OnPropertyChanged(nameof(ChartSeries));
    }

    private static Axis[] CreatePercentageYAxes()
    {
        return
        [
            new Axis
            {
                Name = "%",
                Position = LiveChartsCore.Measure.AxisPosition.End,
                MinLimit = 0,
                MaxLimit = 100,
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                Labeler = value => $"{value:F0}%",
                ShowSeparatorLines = false
            }
        ];
    }

    private static int GetTimeRangeSeconds(TimeRange range) => range switch
    {
        TimeRange.OneMinute => 60,
        TimeRange.FiveMinutes => 300,
        TimeRange.FifteenMinutes => 900,
        TimeRange.OneHour => 3600,
        _ => 60
    };

    #endregion

    #region Partial Methods for Property Changes

    partial void OnShowCpuOverlayChanged(bool value)
    {
        if (!value)
        {
            _cpuOverlayPoints.Clear();
        }
        RebuildChartSeries();
    }

    partial void OnShowMemoryOverlayChanged(bool value)
    {
        if (!value)
        {
            _memoryOverlayPoints.Clear();
        }
        RebuildChartSeries();
    }

    partial void OnShowAdvancedAdaptersChanged(bool value)
    {
        LoadAdapters();
    }

    partial void OnSelectedAdapterChanged(AdapterDisplayItem? value)
    {
        if (value != null)
        {
            _networkMonitor.SetAdapter(value.Id);
        }
        else
        {
            _networkMonitor.SetAdapter(string.Empty);
        }
    }

    #endregion

    #region Adapter Management

    private void LoadAdapters()
    {
        var networkAdapters = _networkMonitor.GetAdapters(ShowAdvancedAdapters)
            .Where(a => a.IsActive)
            .ToList();

        Adapters.Clear();

        foreach (var adapter in networkAdapters)
        {
            var displayItem = new AdapterDisplayItem(adapter);
            Adapters.Add(displayItem);
        }
    }

    #endregion

    #region Data Loading

    private async Task LoadTodayUsageAsync()
    {
        if (_dataPersistence == null) return;

        try
        {
            var (received, sent) = await _dataPersistence.GetTodayUsageAsync();
            _todayStoredReceived = received;
            _todayStoredSent = sent;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to load stored usage data");
        }
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void ToggleCpuOverlay()
    {
        ShowCpuOverlay = !ShowCpuOverlay;
    }

    [RelayCommand]
    private void ToggleMemoryOverlay()
    {
        ShowMemoryOverlay = !ShowMemoryOverlay;
    }

    [RelayCommand]
    private void SetTimeRange(TimeRange range)
    {
        SelectedTimeRange = range;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _networkMonitor.StatsUpdated -= OnNetworkStatsUpdated;
        _systemMonitor.StatsUpdated -= OnSystemStatsUpdated;
    }
}

/// <summary>
/// Display item for time range selection
/// </summary>
public sealed record TimeRangeDisplayItem(
    TimeRange Value,
    string Label,
    string Description,
    int Seconds);

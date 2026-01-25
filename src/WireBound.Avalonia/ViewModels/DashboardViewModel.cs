using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using WireBound.Avalonia.Helpers;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;
using System.Collections.Concurrent;

namespace WireBound.Avalonia.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly INetworkMonitorService _networkMonitor;
    private readonly INavigationService _navigationService;
    private readonly IDataPersistenceService? _dataPersistence;
    private readonly ILogger<DashboardViewModel>? _logger;
    private bool _disposed;
    private readonly ObservableCollection<DateTimePoint> _downloadSpeedPoints = [];
    private readonly ObservableCollection<DateTimePoint> _uploadSpeedPoints = [];

    private readonly ChartDataManager _chartDataManager = new(maxBufferSize: 3600, maxDisplayPoints: 300);

    // Trend tracking using shared calculator
    private readonly TrendIndicatorCalculator _downloadTrendCalculator = new(iconStyle: TrendIconStyle.Geometric);
    private readonly TrendIndicatorCalculator _uploadTrendCalculator = new(iconStyle: TrendIconStyle.Geometric);

    private readonly AdaptiveThresholdCalculator _thresholdCalculator = new(windowSize: 60, smoothingFactor: 0.1);

    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _uploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _downloadTrendIcon = "";

    [ObservableProperty]
    private string _downloadTrendText = "stable";

    [ObservableProperty]
    private string _uploadTrendIcon = "";

    [ObservableProperty]
    private string _uploadTrendText = "stable";

    [ObservableProperty]
    private string _sessionDownload = "0 B";

    [ObservableProperty]
    private string _sessionUpload = "0 B";

    /// <summary>
    /// Today's total downloaded bytes (from database + current session delta)
    /// </summary>
    [ObservableProperty]
    private string _todayDownload = "0 B";

    /// <summary>
    /// Today's total uploaded bytes (from database + current session delta)
    /// </summary>
    [ObservableProperty]
    private string _todayUpload = "0 B";

    /// <summary>
    /// Track today's stored bytes (from database at startup)
    /// </summary>
    private long _todayStoredReceived;
    private long _todayStoredSent;

    [ObservableProperty]
    private string _selectedAdapterName = "All Adapters";

    [ObservableProperty]
    private ObservableCollection<NetworkAdapter> _adapters = [];

    [ObservableProperty]
    private NetworkAdapter? _selectedAdapter;

    [ObservableProperty]
    private TimeRangeOption _selectedTimeRange;

    public ObservableCollection<TimeRangeOption> TimeRangeOptions { get; } =
    [
        new() { Label = "30s", Seconds = 30, Description = "Last 30 seconds" },
        new() { Label = "1m", Seconds = 60, Description = "Last 1 minute" },
        new() { Label = "5m", Seconds = 300, Description = "Last 5 minutes" },
        new() { Label = "15m", Seconds = 900, Description = "Last 15 minutes" },
        new() { Label = "1h", Seconds = 3600, Description = "Last 1 hour" }
    ];

    [ObservableProperty]
    private string _peakDownloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _peakUploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _averageDownloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _averageUploadSpeed = "0 B/s";

    [ObservableProperty]
    private bool _isUpdatesPaused;

    [ObservableProperty]
    private string _pauseStatusText = "";

    [ObservableProperty]
    private bool _showAdvancedAdapters;

    [ObservableProperty]
    private RectangularSection[] _thresholdSections = [];

    // === VPN Traffic Properties ===

    /// <summary>
    /// Whether a VPN adapter is connected (used for panel visibility)
    /// </summary>
    [ObservableProperty]
    private bool _isVpnConnected;

    /// <summary>
    /// VPN download speed
    /// </summary>
    [ObservableProperty]
    private string _vpnDownloadSpeed = "0 B/s";

    /// <summary>
    /// VPN upload speed
    /// </summary>
    [ObservableProperty]
    private string _vpnUploadSpeed = "0 B/s";

    /// <summary>
    /// VPN session download total
    /// </summary>
    [ObservableProperty]
    private string _vpnSessionDownload = "0 B";

    /// <summary>
    /// VPN session upload total
    /// </summary>
    [ObservableProperty]
    private string _vpnSessionUpload = "0 B";

    /// <summary>
    /// Names of active VPN adapters
    /// </summary>
    [ObservableProperty]
    private string _activeVpnNames = "";

    // === Adapter Dashboard Properties ===

    /// <summary>
    /// Whether to show the adapter dashboard panel
    /// </summary>
    [ObservableProperty]
    private bool _showAdapterDashboard = true;

    /// <summary>
    /// Collection of adapters with their current stats for the dashboard
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AdapterDisplayItem> _adapterDisplayItems = [];

    /// <summary>
    /// Cache for tracking per-adapter today's stored bytes (from database at startup)
    /// </summary>
    private readonly ConcurrentDictionary<string, (long Download, long Upload)> _adapterTodayStoredBytes = new();

    /// <summary>
    /// WiFi service for fetching WiFi info
    /// </summary>
    private readonly IWiFiInfoService? _wifiService;

    // Sparkline data collections (separate from main chart)
    private readonly ObservableCollection<DateTimePoint> _downloadSparklinePoints = [];
    private readonly ObservableCollection<DateTimePoint> _uploadSparklinePoints = [];
    private const int SparklineMaxPoints = 30; // 30 seconds of data

    public ISeries[] SpeedSeries { get; }

    /// <summary>
    /// Download sparkline series for the inline card chart
    /// </summary>
    public ISeries[] DownloadSparklineSeries { get; }

    /// <summary>
    /// Upload sparkline series for the inline card chart
    /// </summary>
    public ISeries[] UploadSparklineSeries { get; }

    /// <summary>
    /// X-axis configuration for sparklines (hidden)
    /// </summary>
    public Axis[] SparklineXAxes { get; }

    /// <summary>
    /// Y-axis configuration for download sparkline (separate to allow independent scaling)
    /// </summary>
    public Axis[] DownloadSparklineYAxes { get; }

    /// <summary>
    /// Y-axis configuration for upload sparkline (separate to allow independent scaling)
    /// </summary>
    public Axis[] UploadSparklineYAxes { get; }

    /// <summary>
    /// Draw margin for sparklines (minimal padding)
    /// </summary>
    public LiveChartsCore.Measure.Margin SparklineDrawMargin { get; } = new(0, 0, 0, 0);

    /// <summary>
    /// Draw margin frame for sparklines (no visible frame)
    /// </summary>
    public DrawMarginFrame SparklineDrawMarginFrame { get; } = new()
    {
        Stroke = null,
        Fill = null
    };

    public Axis[] XAxes { get; }

    public Axis[] YAxes { get; } = ChartSeriesFactory.CreateSpeedYAxes();

    public DashboardViewModel(
        INetworkPollingBackgroundService pollingService,
        INetworkMonitorService networkMonitor,
        INavigationService navigationService,
        IDataPersistenceService? dataPersistence = null,
        IWiFiInfoService? wifiService = null,
        ILogger<DashboardViewModel>? logger = null)
    {
        _networkMonitor = networkMonitor;
        _navigationService = navigationService;
        _dataPersistence = dataPersistence;
        _wifiService = wifiService;
        _logger = logger;

        // Subscribe to stats updates
        networkMonitor.StatsUpdated += OnStatsUpdated;

        // Initialize X-axis using factory
        XAxes = ChartSeriesFactory.CreateTimeXAxes();

        _selectedTimeRange = TimeRangeOptions[1]; // 1 minute default

        // Initialize chart series using factory
        SpeedSeries = ChartSeriesFactory.CreateSpeedLineSeries(_downloadSpeedPoints, _uploadSpeedPoints);

        // Initialize sparkline series and axes (minimal inline charts)
        DownloadSparklineSeries = ChartSeriesFactory.CreateSparklineSeries(_downloadSparklinePoints, isDownload: true);
        UploadSparklineSeries = ChartSeriesFactory.CreateSparklineSeries(_uploadSparklinePoints, isDownload: false);
        SparklineXAxes = ChartSeriesFactory.CreateSparklineAxes(isXAxis: true);
        DownloadSparklineYAxes = ChartSeriesFactory.CreateSparklineAxes(isXAxis: false);
        UploadSparklineYAxes = ChartSeriesFactory.CreateSparklineAxes(isXAxis: false);

        // Load adapters
        LoadAdapters(networkMonitor);

        // Initialize adapter display items
        LoadAdapterDisplayItems();

        // Load today's stored usage from database
        _ = LoadTodayUsageAsync();
    }

    /// <summary>
    /// Loads today's usage from database to add to current session
    /// </summary>
    private async Task LoadTodayUsageAsync()
    {
        if (_dataPersistence == null) return;

        try
        {
            // Load total today's usage
            var (received, sent) = await _dataPersistence.GetTodayUsageAsync();
            _todayStoredReceived = received;
            _todayStoredSent = sent;

            // Load per-adapter today's usage
            var adapterUsage = await _dataPersistence.GetTodayUsageByAdapterAsync();
            foreach (var (adapterId, (download, upload)) in adapterUsage)
            {
                _adapterTodayStoredBytes[adapterId] = (download, upload);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Failed to load stored usage data");
        }
    }

    /// <summary>
    /// Loads adapter display items for the dashboard panel
    /// </summary>
    private void LoadAdapterDisplayItems()
    {
        var adapters = _networkMonitor.GetAdapters(ShowAdvancedAdapters)
            .Where(a => a.IsActive) // Only show active adapters
            .ToList();

        AdapterDisplayItems.Clear();

        foreach (var adapter in adapters)
        {
            var displayItem = new AdapterDisplayItem(adapter);

            // Fetch WiFi info for wireless adapters
            if (adapter.AdapterType == NetworkAdapterType.WiFi && _wifiService?.IsSupported == true)
            {
                try
                {
                    var wifiInfo = _wifiService.GetWiFiInfo(adapter.Id);
                    displayItem.WiFiInfo = wifiInfo;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Failed to fetch WiFi info for adapter {AdapterId}", adapter.Id);
                }
            }

            AdapterDisplayItems.Add(displayItem);
        }
    }

    /// <summary>
    /// Updates adapter display items with current traffic stats
    /// </summary>
    private void UpdateAdapterDisplayItems()
    {
        var allStats = _networkMonitor.GetAllAdapterStats();

        foreach (var item in AdapterDisplayItems)
        {
            if (allStats.TryGetValue(item.Id, out var stats))
            {
                // Get stored today's bytes for this adapter (from database at startup)
                _adapterTodayStoredBytes.TryGetValue(item.Id, out var storedToday);

                item.UpdateTraffic(
                    stats.DownloadSpeedBps,
                    stats.UploadSpeedBps,
                    stats.SessionBytesReceived,
                    stats.SessionBytesSent,
                    storedToday.Download,
                    storedToday.Upload
                );
            }
        }
    }

    private void OnStatsUpdated(object? sender, NetworkStats stats)
    {
        if (_disposed) return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            UpdateUI(stats);
        });
    }

    private void UpdateUI(NetworkStats stats)
    {
        var now = DateTime.Now;

        DownloadSpeed = ByteFormatter.FormatSpeed(stats.DownloadSpeedBps);
        UploadSpeed = ByteFormatter.FormatSpeed(stats.UploadSpeedBps);
        SessionDownload = ByteFormatter.FormatBytes(stats.SessionBytesReceived);
        SessionUpload = ByteFormatter.FormatBytes(stats.SessionBytesSent);

        // Update trend indicators
        UpdateTrendIndicators(stats.DownloadSpeedBps, stats.UploadSpeedBps);

        // Calculate today's totals (stored from previous sessions + current session)
        var todayReceivedTotal = _todayStoredReceived + stats.SessionBytesReceived;
        var todaySentTotal = _todayStoredSent + stats.SessionBytesSent;
        TodayDownload = ByteFormatter.FormatBytes(todayReceivedTotal);
        TodayUpload = ByteFormatter.FormatBytes(todaySentTotal);

        // Update VPN connection status
        IsVpnConnected = stats.IsVpnConnected;

        // Update VPN display data when connected
        if (stats.IsVpnConnected)
        {
            // Show connected adapters, or active ones if there's traffic
            ActiveVpnNames = stats.ActiveVpnAdapters.Count > 0
                ? string.Join(", ", stats.ActiveVpnAdapters)
                : string.Join(", ", stats.ConnectedVpnAdapters);

            // Update VPN traffic speeds and session totals
            VpnDownloadSpeed = ByteFormatter.FormatSpeed(stats.VpnDownloadSpeedBps);
            VpnUploadSpeed = ByteFormatter.FormatSpeed(stats.VpnUploadSpeedBps);
            VpnSessionDownload = ByteFormatter.FormatBytes(stats.VpnSessionBytesReceived);
            VpnSessionUpload = ByteFormatter.FormatBytes(stats.VpnSessionBytesSent);
        }

        // Update adapter dashboard items
        if (ShowAdapterDashboard && AdapterDisplayItems.Count > 0)
        {
            UpdateAdapterDisplayItems();
        }

        // Add to buffer and update statistics via ChartDataManager
        _chartDataManager.AddDataPoint(now, stats.DownloadSpeedBps, stats.UploadSpeedBps);

        // Update statistics display
        UpdateStatisticsDisplay();

        // Update chart if not paused
        if (!IsUpdatesPaused)
        {
            UpdateChart(now, stats.DownloadSpeedBps, stats.UploadSpeedBps);
        }

        // Always update sparklines (they're always visible on the dashboard)
        UpdateSparklines(now, stats.DownloadSpeedBps, stats.UploadSpeedBps);
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

    private void UpdateSparklines(DateTime now, long downloadBps, long uploadBps)
    {
        var cutoff = now.AddSeconds(-SparklineMaxPoints);

        // Add new points
        _downloadSparklinePoints.Add(new DateTimePoint(now, downloadBps));
        _uploadSparklinePoints.Add(new DateTimePoint(now, uploadBps));

        // Remove old points
        while (_downloadSparklinePoints.Count > 0 && _downloadSparklinePoints[0].DateTime < cutoff)
        {
            _downloadSparklinePoints.RemoveAt(0);
        }
        while (_uploadSparklinePoints.Count > 0 && _uploadSparklinePoints[0].DateTime < cutoff)
        {
            _uploadSparklinePoints.RemoveAt(0);
        }

        // Update X-axis range for sparklines
        if (SparklineXAxes.Length > 0)
        {
            SparklineXAxes[0].MinLimit = cutoff.Ticks;
            SparklineXAxes[0].MaxLimit = now.Ticks;
        }

        // Update Y-axis max to be based on peak (prevents constant resizing)
        // Use a stable max: the session peak + 20% headroom
        if (DownloadSparklineYAxes.Length > 0)
        {
            var downloadMax = Math.Max(_chartDataManager.PeakDownloadBps * 1.2, 1024); // At least 1 KB/s scale
            DownloadSparklineYAxes[0].MaxLimit = downloadMax;
        }
        if (UploadSparklineYAxes.Length > 0)
        {
            var uploadMax = Math.Max(_chartDataManager.PeakUploadBps * 1.2, 1024); // At least 1 KB/s scale
            UploadSparklineYAxes[0].MaxLimit = uploadMax;
        }
    }

    private void UpdateChart(DateTime now, long downloadBps, long uploadBps)
    {
        var rangeSeconds = SelectedTimeRange?.Seconds ?? 60;
        var cutoff = now.AddSeconds(-rangeSeconds);

        // Add new point
        _downloadSpeedPoints.Add(new DateTimePoint(now, downloadBps));
        _uploadSpeedPoints.Add(new DateTimePoint(now, uploadBps));

        // Remove old points
        while (_downloadSpeedPoints.Count > 0 && _downloadSpeedPoints[0].DateTime < cutoff)
        {
            _downloadSpeedPoints.RemoveAt(0);
        }
        while (_uploadSpeedPoints.Count > 0 && _uploadSpeedPoints[0].DateTime < cutoff)
        {
            _uploadSpeedPoints.RemoveAt(0);
        }

        // Update X-axis range
        if (XAxes.Length > 0)
        {
            XAxes[0].MinLimit = cutoff.Ticks;
            XAxes[0].MaxLimit = now.Ticks;
        }
    }

    private void UpdateStatisticsDisplay()
    {
        PeakDownloadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.PeakDownloadBps);
        PeakUploadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.PeakUploadBps);
        AverageDownloadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.AverageDownloadBps);
        AverageUploadSpeed = ByteFormatter.FormatSpeed(_chartDataManager.AverageUploadBps);

        UpdateAdaptiveThresholds(Math.Max(_chartDataManager.PeakDownloadBps, _chartDataManager.PeakUploadBps));
    }

    private void UpdateAdaptiveThresholds(long maxSpeedBps)
    {
        _thresholdCalculator.Update(maxSpeedBps);
        var (quarter, half, threeQuarter, full) = _thresholdCalculator.GetThresholdLevels();

        var dashedPaint = new SolidColorPaint(ChartColors.GridLineColor)
        {
            StrokeThickness = 1,
            PathEffect = new DashEffect([4, 4])
        };

        ThresholdSections =
        [
            new RectangularSection { Yi = quarter, Yj = quarter, Stroke = dashedPaint },
            new RectangularSection
            {
                Yi = half, Yj = half,
                Stroke = new SolidColorPaint(ChartColors.SectionStrokeColor)
                {
                    StrokeThickness = 1,
                    PathEffect = new DashEffect([4, 4])
                }
            },
            new RectangularSection { Yi = threeQuarter, Yj = threeQuarter, Stroke = dashedPaint },
            new RectangularSection
            {
                Yi = full, Yj = full,
                Stroke = new SolidColorPaint(ChartColors.WarningSectionColor)
                {
                    StrokeThickness = 1.5f,
                    PathEffect = new DashEffect([6, 3])
                }
            }
        ];
    }

    private void LoadAdapters(INetworkMonitorService networkMonitor)
    {
        var currentSelection = SelectedAdapter?.Id;

        Adapters.Clear();
        var allAdapters = new NetworkAdapter { Id = "", Name = "All Adapters", DisplayName = "All Adapters", Category = "System" };
        Adapters.Add(allAdapters);

        foreach (var adapter in networkMonitor.GetAdapters(ShowAdvancedAdapters))
        {
            Adapters.Add(adapter);
        }

        // Restore selection if still available, otherwise select "All Adapters"
        SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == currentSelection) ?? allAdapters;
    }

    partial void OnShowAdvancedAdaptersChanged(bool value)
    {
        LoadAdapters(_networkMonitor);
    }

    partial void OnSelectedAdapterChanged(NetworkAdapter? value)
    {
        if (value != null)
        {
            _networkMonitor.SetAdapter(value.Id);
            SelectedAdapterName = value.DisplayName;
            ClearChart();
        }
    }

    partial void OnSelectedTimeRangeChanged(TimeRangeOption value)
    {
        RefreshChartFromBuffer();
    }

    private void RefreshChartFromBuffer()
    {
        if (SelectedTimeRange == null) return;

        var (downloadPoints, uploadPoints) = _chartDataManager.GetDisplayData(SelectedTimeRange.Seconds);

        _downloadSpeedPoints.Clear();
        _uploadSpeedPoints.Clear();

        foreach (var point in downloadPoints)
            _downloadSpeedPoints.Add(point);
        foreach (var point in uploadPoints)
            _uploadSpeedPoints.Add(point);
    }

    private void ClearChart()
    {
        _downloadSpeedPoints.Clear();
        _uploadSpeedPoints.Clear();
        _chartDataManager.Clear();
        ResetStatisticsDisplay();
    }

    private void ResetStatisticsDisplay()
    {
        PeakDownloadSpeed = "0 B/s";
        PeakUploadSpeed = "0 B/s";
        AverageDownloadSpeed = "0 B/s";
        AverageUploadSpeed = "0 B/s";
        _thresholdCalculator.Reset();
        ThresholdSections = [];
    }

    [RelayCommand]
    private void ReturnToLive()
    {
        IsUpdatesPaused = false;
        PauseStatusText = "";
        var now = DateTime.Now;
        var rangeSeconds = SelectedTimeRange?.Seconds ?? 60;
        if (XAxes.Length > 0)
        {
            XAxes[0].MinLimit = now.AddSeconds(-rangeSeconds).Ticks;
            XAxes[0].MaxLimit = now.Ticks;
        }
    }

    [RelayCommand]
    private void NavigateToCharts()
    {
        _navigationService.NavigateTo("Charts");
    }

    [RelayCommand]
    private void NavigateToHistory()
    {
        _navigationService.NavigateTo("History");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _networkMonitor.StatsUpdated -= OnStatsUpdated;
    }
}

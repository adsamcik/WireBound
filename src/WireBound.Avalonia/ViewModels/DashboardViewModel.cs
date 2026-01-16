using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using WireBound.Avalonia.Services;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;
using System.Collections.Concurrent;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Represents a time range option for the chart display
/// </summary>
public sealed class TimeRangeOption
{
    public required string Label { get; init; }
    public required int Seconds { get; init; }
    public required string Description { get; init; }
}

public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly INetworkMonitorService _networkMonitor;
    private readonly INavigationService _navigationService;
    private bool _disposed;
    private readonly ObservableCollection<DateTimePoint> _downloadSpeedPoints = [];
    private readonly ObservableCollection<DateTimePoint> _uploadSpeedPoints = [];
    
    private readonly List<(DateTime Time, long Download, long Upload)> _dataBuffer = [];
    private const int MaxBufferSize = 3600;
    private const int MaxDisplayPoints = 300;
    
    private long _peakDownloadBps;
    private long _peakUploadBps;
    private long _totalDownloadBps;
    private long _totalUploadBps;
    private int _sampleCount;
    
    private readonly AdaptiveThresholdCalculator _thresholdCalculator = new(windowSize: 60, smoothingFactor: 0.1);

    [ObservableProperty]
    private string _downloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _uploadSpeed = "0 B/s";

    [ObservableProperty]
    private string _sessionDownload = "0 B";

    [ObservableProperty]
    private string _sessionUpload = "0 B";

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
    /// Cache for tracking per-adapter session bytes
    /// </summary>
    private readonly ConcurrentDictionary<string, (long Download, long Upload)> _adapterSessionBytes = new();
    
    /// <summary>
    /// WiFi service for fetching WiFi info
    /// </summary>
    private readonly IWiFiInfoService? _wifiService;

    public ISeries[] SpeedSeries { get; }

    public Axis[] XAxes { get; }
    
    public Axis[] YAxes { get; } =
    [
        new Axis
        {
            Name = "Speed",
            NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
            LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
            TextSize = 11,
            NameTextSize = 12,
            MinLimit = 0,
            SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
            Labeler = value => ByteFormatter.FormatSpeed((long)value)
        }
    ];

    public DashboardViewModel(
        INetworkPollingBackgroundService pollingService,
        INetworkMonitorService networkMonitor,
        INavigationService navigationService,
        IWiFiInfoService? wifiService = null)
    {
        _networkMonitor = networkMonitor;
        _navigationService = navigationService;
        _wifiService = wifiService;

        // Subscribe to stats updates
        networkMonitor.StatsUpdated += OnStatsUpdated;
        
        // Initialize X-axis
        XAxes =
        [
            new DateTimeAxis(TimeSpan.FromSeconds(1), date => date.ToString("HH:mm:ss"))
            {
                Name = "Time",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                NameTextSize = 12,
                AnimationsSpeed = TimeSpan.Zero,
                MinStep = TimeSpan.FromSeconds(2).Ticks,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
            }
        ];
        
        _selectedTimeRange = TimeRangeOptions[1]; // 1 minute default

        // Initialize chart series
        var downloadColor = ChartColors.DownloadAccentColor;
        var uploadColor = ChartColors.UploadAccentColor;

        SpeedSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Name = "Download",
                Values = _downloadSpeedPoints,
                Fill = new LinearGradientPaint(
                    [downloadColor.WithAlpha(100), downloadColor.WithAlpha(0)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1)
                ),
                Stroke = new SolidColorPaint(downloadColor, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 1,
                AnimationsSpeed = TimeSpan.Zero
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Upload",
                Values = _uploadSpeedPoints,
                Fill = new LinearGradientPaint(
                    [uploadColor.WithAlpha(100), uploadColor.WithAlpha(0)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1)
                ),
                Stroke = new SolidColorPaint(uploadColor, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 1,
                AnimationsSpeed = TimeSpan.Zero
            }
        ];

        // Load adapters
        LoadAdapters(networkMonitor);
        
        // Initialize adapter display items
        LoadAdapterDisplayItems();
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
                catch
                {
                    // Ignore WiFi info fetch errors
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
                item.UpdateTraffic(
                    stats.DownloadSpeedBps,
                    stats.UploadSpeedBps,
                    stats.SessionBytesReceived,
                    stats.SessionBytesSent
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

        // Add to buffer
        _dataBuffer.Add((now, stats.DownloadSpeedBps, stats.UploadSpeedBps));
        if (_dataBuffer.Count > MaxBufferSize)
        {
            _dataBuffer.RemoveAt(0);
        }

        // Update statistics
        UpdateStatistics(stats.DownloadSpeedBps, stats.UploadSpeedBps);

        // Update chart if not paused
        if (!IsUpdatesPaused)
        {
            UpdateChart(now, stats.DownloadSpeedBps, stats.UploadSpeedBps);
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

    private void UpdateStatistics(long downloadBps, long uploadBps)
    {
        if (downloadBps > _peakDownloadBps)
        {
            _peakDownloadBps = downloadBps;
            PeakDownloadSpeed = ByteFormatter.FormatSpeed(_peakDownloadBps);
        }
        if (uploadBps > _peakUploadBps)
        {
            _peakUploadBps = uploadBps;
            PeakUploadSpeed = ByteFormatter.FormatSpeed(_peakUploadBps);
        }

        _totalDownloadBps += downloadBps;
        _totalUploadBps += uploadBps;
        _sampleCount++;

        if (_sampleCount > 0)
        {
            AverageDownloadSpeed = ByteFormatter.FormatSpeed(_totalDownloadBps / _sampleCount);
            AverageUploadSpeed = ByteFormatter.FormatSpeed(_totalUploadBps / _sampleCount);
        }

        UpdateAdaptiveThresholds(Math.Max(downloadBps, uploadBps));
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

        var cutoff = DateTime.Now.AddSeconds(-SelectedTimeRange.Seconds);
        var relevantData = _dataBuffer.Where(d => d.Time >= cutoff).ToList();

        var downloadPoints = relevantData.Select(d => new DateTimePoint(d.Time, d.Download)).ToList();
        var uploadPoints = relevantData.Select(d => new DateTimePoint(d.Time, d.Upload)).ToList();

        if (downloadPoints.Count > MaxDisplayPoints)
        {
            downloadPoints = LttbDownsampler.Downsample(downloadPoints, MaxDisplayPoints);
            uploadPoints = LttbDownsampler.Downsample(uploadPoints, MaxDisplayPoints);
        }

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
        _dataBuffer.Clear();
        ResetStatistics();
    }

    private void ResetStatistics()
    {
        _peakDownloadBps = 0;
        _peakUploadBps = 0;
        _totalDownloadBps = 0;
        _totalUploadBps = 0;
        _sampleCount = 0;
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

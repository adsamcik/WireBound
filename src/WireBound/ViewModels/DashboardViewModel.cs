using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.Timers;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.ViewModels;

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
    private readonly INetworkMonitorService _networkMonitor;    private readonly IProcessNetworkService? _processNetworkService;    private bool _disposed;
    private readonly ObservableCollection<DateTimePoint> _downloadSpeedPoints = new();
    private readonly ObservableCollection<DateTimePoint> _uploadSpeedPoints = new();
    
    // Data buffer for historical data (1 hour = 3600 seconds)
    private readonly List<(DateTime Time, long Download, long Upload)> _dataBuffer = new();
    private const int MaxBufferSize = 3600;
    
    // Statistics tracking
    private long _peakDownloadBps;
    private long _peakUploadBps;
    private long _totalDownloadBps;
    private long _totalUploadBps;
    private int _sampleCount;
    
    // Adaptive threshold calculator
    private readonly AdaptiveThresholdCalculator _thresholdCalculator = new(windowSize: 60, smoothingFactor: 0.1);
    
    // Auto-pause functionality
    private readonly System.Timers.Timer _interactionTimer;
    private bool _isUserInteracting;
    private const int InteractionResumeDelayMs = 3000; // 3 seconds
    
    // LTTB downsampling
    private const int MaxDisplayPoints = 300;

    [ObservableProperty]
    public partial string DownloadSpeed { get; set; }

    [ObservableProperty]
    public partial string UploadSpeed { get; set; }

    [ObservableProperty]
    public partial string SessionDownload { get; set; }

    [ObservableProperty]
    public partial string SessionUpload { get; set; }

    [ObservableProperty]
    public partial string SelectedAdapterName { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<NetworkAdapter> Adapters { get; set; }

    [ObservableProperty]
    public partial NetworkAdapter? SelectedAdapter { get; set; }
    
    // Time range selection
    [ObservableProperty]
    public partial TimeRangeOption SelectedTimeRange { get; set; }
    
    public ObservableCollection<TimeRangeOption> TimeRangeOptions { get; } =
    [
        new() { Label = "30s", Seconds = 30, Description = "Last 30 seconds" },
        new() { Label = "1m", Seconds = 60, Description = "Last 1 minute" },
        new() { Label = "5m", Seconds = 300, Description = "Last 5 minutes" },
        new() { Label = "15m", Seconds = 900, Description = "Last 15 minutes" },
        new() { Label = "1h", Seconds = 3600, Description = "Last 1 hour" }
    ];
    
    // Statistics
    [ObservableProperty]
    public partial string PeakDownloadSpeed { get; set; }
    
    [ObservableProperty]
    public partial string PeakUploadSpeed { get; set; }
    
    [ObservableProperty]
    public partial string AverageDownloadSpeed { get; set; }
    
    [ObservableProperty]
    public partial string AverageUploadSpeed { get; set; }
    
    // Auto-pause state
    [ObservableProperty]
    public partial bool IsUpdatesPaused { get; set; }
    
    [ObservableProperty]
    public partial string PauseStatusText { get; set; }
    
    // Adaptive threshold sections for chart
    [ObservableProperty]
    public partial RectangularSection[] ThresholdSections { get; set; }
    
    // Per-app tracking
    [ObservableProperty]
    public partial ObservableCollection<ProcessNetworkStats> TopApps { get; set; }
    
    [ObservableProperty]
    public partial bool IsPerAppTrackingEnabled { get; set; }

    public ISeries[] SpeedSeries { get; }

    public Axis[] XAxes { get; }
    
    private Axis CreateXAxis()
    {
        return new DateTimeAxis(TimeSpan.FromSeconds(1), date => date.ToString("HH:mm:ss"))
        {
            Name = "Time",
            NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
            LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
            TextSize = 11,
            NameTextSize = 12,
            AnimationsSpeed = TimeSpan.FromMilliseconds(0),
            MinStep = TimeSpan.FromSeconds(2).Ticks,
            SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
            // Fixed time window - will be updated dynamically
            MinLimit = null,
            MaxLimit = null
        };
    }

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

    public DashboardViewModel(INetworkMonitorService networkMonitor, IServiceProvider serviceProvider)
    {
        _networkMonitor = networkMonitor;
        _processNetworkService = serviceProvider.GetService<IProcessNetworkService>();
        _networkMonitor.StatsUpdated += OnStatsUpdated;
        
        // Initialize X-axis
        XAxes = [CreateXAxis()];

        // Initialize observable properties
        DownloadSpeed = "0 B/s";
        UploadSpeed = "0 B/s";
        SessionDownload = "0 B";
        SessionUpload = "0 B";
        SelectedAdapterName = "All Adapters";
        Adapters = new ObservableCollection<NetworkAdapter>();
        TopApps = new ObservableCollection<ProcessNetworkStats>();
        
        // Initialize statistics
        PeakDownloadSpeed = "0 B/s";
        PeakUploadSpeed = "0 B/s";
        AverageDownloadSpeed = "0 B/s";
        AverageUploadSpeed = "0 B/s";
        
        // Default time range
        SelectedTimeRange = TimeRangeOptions[1]; // 1 minute
        
        // Initialize per-app tracking if available
        InitializePerAppTracking();

        // Use centralized chart colors for consistency
        var downloadColor = ChartColors.DownloadAccentColor;
        var uploadColor = ChartColors.UploadAccentColor;

        // Initialize chart series with gradient fills and smooth curves
        SpeedSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Name = "Download",
                Values = _downloadSpeedPoints,
                Fill = new LiveChartsCore.SkiaSharpView.Painting.LinearGradientPaint(
                    [downloadColor.WithAlpha(100), downloadColor.WithAlpha(0)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1)
                ),
                Stroke = new SolidColorPaint(downloadColor, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 1,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0)
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Upload",
                Values = _uploadSpeedPoints,
                Fill = new LiveChartsCore.SkiaSharpView.Painting.LinearGradientPaint(
                    [uploadColor.WithAlpha(100), uploadColor.WithAlpha(0)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1)
                ),
                Stroke = new SolidColorPaint(uploadColor, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 1,
                AnimationsSpeed = TimeSpan.FromMilliseconds(0)
            }
        ];

        // Initialize threshold sections (empty initially)
        ThresholdSections = [];
        PauseStatusText = "";
        
        // Initialize auto-pause timer
        _interactionTimer = new System.Timers.Timer(InteractionResumeDelayMs);
        _interactionTimer.AutoReset = false;
        _interactionTimer.Elapsed += OnInteractionTimerElapsed;

        LoadAdapters();
    }
    
    /// <summary>
    /// Call this when user interacts with the chart (zoom, pan, etc.)
    /// </summary>
    public void NotifyChartInteraction()
    {
        _isUserInteracting = true;
        IsUpdatesPaused = true;
        PauseStatusText = "⏸ Updates paused";
        
        // Reset and restart the timer
        _interactionTimer.Stop();
        _interactionTimer.Start();
    }
    
    private void OnInteractionTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _isUserInteracting = false;
            IsUpdatesPaused = false;
            PauseStatusText = "";
            ResetChartToLive();
        });
    }
    
    /// <summary>
    /// Returns the chart to live view, resetting zoom/pan and resuming updates
    /// </summary>
    [RelayCommand]
    private void ReturnToLive()
    {
        _interactionTimer.Stop();
        _isUserInteracting = false;
        IsUpdatesPaused = false;
        PauseStatusText = "";
        ResetChartToLive();
    }
    
    /// <summary>
    /// Navigate to the Charts page
    /// </summary>
    [RelayCommand]
    private async Task NavigateToChartsAsync()
    {
        await Shell.Current.GoToAsync("//Charts");
    }
    
    /// <summary>
    /// Navigate to the History page
    /// </summary>
    [RelayCommand]
    private async Task NavigateToHistoryAsync()
    {
        await Shell.Current.GoToAsync("//History");
    }
    
    /// <summary>
    /// Resets the chart X-axis to show live data with fixed time range
    /// </summary>
    private void ResetChartToLive()
    {
        var now = DateTime.Now;
        var rangeSeconds = SelectedTimeRange?.Seconds ?? 60;
        
        // Set fixed time range on X-axis
        if (XAxes.Length > 0)
        {
            XAxes[0].MinLimit = now.AddSeconds(-rangeSeconds).Ticks;
            XAxes[0].MaxLimit = now.Ticks;
        }
    }
    
    [RelayCommand]
    private void SelectTimeRange(string label)
    {
        var range = TimeRangeOptions.FirstOrDefault(r => r.Label == label);
        if (range != null)
        {
            SelectedTimeRange = range;
            RefreshChartFromBuffer();
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
        
        // Convert to DateTimePoints
        var downloadPoints = relevantData.Select(d => new DateTimePoint(d.Time, d.Download)).ToList();
        var uploadPoints = relevantData.Select(d => new DateTimePoint(d.Time, d.Upload)).ToList();
        
        // Apply LTTB downsampling if we have more than MaxDisplayPoints
        if (downloadPoints.Count > MaxDisplayPoints)
        {
            downloadPoints = LttbDownsampler.Downsample(downloadPoints, MaxDisplayPoints);
            uploadPoints = LttbDownsampler.Downsample(uploadPoints, MaxDisplayPoints);
        }
        
        _downloadSpeedPoints.Clear();
        _uploadSpeedPoints.Clear();
        
        foreach (var point in downloadPoints)
        {
            _downloadSpeedPoints.Add(point);
        }
        foreach (var point in uploadPoints)
        {
            _uploadSpeedPoints.Add(point);
        }
    }
    
    private void UpdateStatistics(long downloadBps, long uploadBps)
    {
        // Update peaks
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
        
        // Update averages
        _totalDownloadBps += downloadBps;
        _totalUploadBps += uploadBps;
        _sampleCount++;
        
        if (_sampleCount > 0)
        {
            AverageDownloadSpeed = ByteFormatter.FormatSpeed(_totalDownloadBps / _sampleCount);
            AverageUploadSpeed = ByteFormatter.FormatSpeed(_totalUploadBps / _sampleCount);
        }
        
        // Update adaptive thresholds
        UpdateAdaptiveThresholds(Math.Max(downloadBps, uploadBps));
    }
    
    private void UpdateAdaptiveThresholds(long maxSpeedBps)
    {
        // Update threshold calculator with the maximum speed
        var thresholdMax = _thresholdCalculator.Update(maxSpeedBps);
        var (quarter, half, threeQuarter, full) = _thresholdCalculator.GetThresholdLevels();
        
        // Create dashed paint for threshold lines using centralized colors
        var dashedPaint = new SolidColorPaint(ChartColors.GridLineColor)
        {
            StrokeThickness = 1,
            PathEffect = new DashEffect([4, 4])
        };
        
        // Create threshold sections (horizontal lines)
        ThresholdSections = 
        [
            new RectangularSection
            {
                Yi = quarter,
                Yj = quarter,
                Stroke = dashedPaint
            },
            new RectangularSection
            {
                Yi = half,
                Yj = half,
                Stroke = new SolidColorPaint(ChartColors.SectionStrokeColor)
                {
                    StrokeThickness = 1,
                    PathEffect = new DashEffect([4, 4])
                }
            },
            new RectangularSection
            {
                Yi = threeQuarter,
                Yj = threeQuarter,
                Stroke = dashedPaint
            },
            new RectangularSection
            {
                Yi = full,
                Yj = full,
                Stroke = new SolidColorPaint(ChartColors.WarningSectionColor)
                {
                    StrokeThickness = 1.5f,
                    PathEffect = new DashEffect([6, 3])
                }
            }
        ];
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
        
        // Reset adaptive thresholds
        _thresholdCalculator.Reset();
        ThresholdSections = [];
    }

    private void LoadAdapters()
    {
        Adapters.Clear();
        
        // Add "All Adapters" option
        var allAdapters = new NetworkAdapter { Id = "", Name = "All Adapters" };
        Adapters.Add(allAdapters);
        
        foreach (var adapter in _networkMonitor.GetAdapters())
        {
            Adapters.Add(adapter);
        }

        // Set default selection (will trigger OnSelectedAdapterChanged but that's fine for initial load)
        SelectedAdapter = allAdapters;
    }
    
    private void InitializePerAppTracking()
    {
        if (_processNetworkService == null || !_processNetworkService.IsPlatformSupported)
        {
            IsPerAppTrackingEnabled = false;
            return;
        }
        
        IsPerAppTrackingEnabled = true;
        _processNetworkService.StatsUpdated += OnProcessStatsUpdated;
    }
    
    private void OnProcessStatsUpdated(object? sender, ProcessStatsUpdatedEventArgs e)
    {
        if (_disposed) return;
        
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_disposed) return;
            
            // Get top 5 apps by total speed
            var topApps = e.Stats
                .OrderByDescending(p => p.TotalSpeedBps)
                .Take(5)
                .ToList();
            
            TopApps.Clear();
            foreach (var app in topApps)
            {
                TopApps.Add(app);
            }
        });
    }

    partial void OnSelectedAdapterChanged(NetworkAdapter? value)
    {
        if (value != null)
        {
            _networkMonitor.SetAdapter(value.Id);
            SelectedAdapterName = value.Name;
            _networkMonitor.ResetSession();
            
            // Ensure chart is cleared on UI thread to avoid race conditions
            if (MainThread.IsMainThread)
            {
                ClearChart();
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(ClearChart);
            }
        }
    }

    private void OnStatsUpdated(object? sender, NetworkStats stats)
    {
        // Skip if already disposed to prevent UI updates after cleanup
        if (_disposed) return;

        System.Diagnostics.Debug.WriteLine($"[DashboardVM] StatsUpdated: DL={stats.DownloadSpeedBps} UL={stats.UploadSpeedBps}");

        // Update on UI thread using MAUI dispatcher
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Double-check disposed state on UI thread
            if (_disposed) return;

            DownloadSpeed = stats.DownloadSpeedFormatted;
            UploadSpeed = stats.UploadSpeedFormatted;
            SessionDownload = stats.SessionReceivedFormatted;
            SessionUpload = stats.SessionSentFormatted;
            
            // Update statistics (always update even when paused)
            UpdateStatistics(stats.DownloadSpeedBps, stats.UploadSpeedBps);

            // Always add to buffer regardless of pause state
            var now = stats.Timestamp;
            _dataBuffer.Add((now, stats.DownloadSpeedBps, stats.UploadSpeedBps));
            
            // Keep buffer within size limit
            while (_dataBuffer.Count > MaxBufferSize)
            {
                _dataBuffer.RemoveAt(0);
            }

            // Skip chart updates if user is interacting (auto-pause)
            if (_isUserInteracting) return;

            // Update chart based on selected time range
            var rangeSeconds = SelectedTimeRange?.Seconds ?? 60;
            var cutoff = now.AddSeconds(-rangeSeconds);
            _downloadSpeedPoints.Add(new DateTimePoint(now, stats.DownloadSpeedBps));
            _uploadSpeedPoints.Add(new DateTimePoint(now, stats.UploadSpeedBps));

            // Keep only points within time range
            while (_downloadSpeedPoints.Count > 0 && 
                   _downloadSpeedPoints[0].DateTime < cutoff)
            {
                _downloadSpeedPoints.RemoveAt(0);
                _uploadSpeedPoints.RemoveAt(0);
            }
            
            // Update X-axis to show fixed time window (always ending at now)
            if (XAxes.Length > 0)
            {
                XAxes[0].MinLimit = cutoff.Ticks;
                XAxes[0].MaxLimit = now.Ticks;
            }
        });
    }

    private void ClearChart()
    {
        _downloadSpeedPoints.Clear();
        _uploadSpeedPoints.Clear();
        _dataBuffer.Clear();
        ResetStatistics();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _networkMonitor.StatsUpdated -= OnStatsUpdated;
        
        if (_processNetworkService != null)
        {
            _processNetworkService.StatsUpdated -= OnProcessStatsUpdated;
        }
        
        // Dispose timer
        _interactionTimer.Stop();
        _interactionTimer.Elapsed -= OnInteractionTimerElapsed;
        _interactionTimer.Dispose();
    }
}

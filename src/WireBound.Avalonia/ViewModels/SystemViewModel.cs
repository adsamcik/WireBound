using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
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
/// ViewModel for CPU and RAM monitoring
/// </summary>
public sealed partial class SystemViewModel : ObservableObject, IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ISystemMonitorService _systemMonitorService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<SystemViewModel>? _logger;
    private bool _disposed;
    private bool _isViewActive;
    private const int MaxHistoryPoints = 60; // 1 minute of data at 1 point/second

    // Latest-wins coalescing: only one UI post in-flight at a time
    private SystemStats? _pendingSystemStats;
    private int _systemUpdateQueued;

    // Cached formatted values
    private double _lastCpuPercent = -1;
    private double _lastMemPercent = -1;
    private long _lastMemUsed = -1;
    private long _lastMemTotal = -1;
    private long _lastMemAvailable = -1;

    // CPU Properties
    [ObservableProperty]
    private double _cpuUsagePercent;

    [ObservableProperty]
    private string _cpuUsageFormatted = "0%";

    [ObservableProperty]
    private string _processorName = string.Empty;

    [ObservableProperty]
    private int _processorCount;

    [ObservableProperty]
    private ObservableCollection<double> _perCoreUsage = [];

    [ObservableProperty]
    private double? _cpuFrequencyMhz;

    [ObservableProperty]
    private double? _cpuTemperature;

    [ObservableProperty]
    private bool _isCpuTemperatureAvailable;

    // Memory Properties
    [ObservableProperty]
    private double _memoryUsagePercent;

    [ObservableProperty]
    private string _memoryUsageFormatted = "0%";

    [ObservableProperty]
    private string _memoryUsed = "0 B";

    [ObservableProperty]
    private string _memoryTotal = "0 B";

    [ObservableProperty]
    private string _memoryAvailable = "0 B";

    // Chart data
    [ObservableProperty]
    private BatchObservableCollection<DateTimePoint> _cpuHistoryPoints = new();

    [ObservableProperty]
    private BatchObservableCollection<DateTimePoint> _memoryHistoryPoints = new();

    public ISeries[] CpuSeries { get; }
    public ISeries[] MemorySeries { get; }

    /// <summary>
    /// X-axis configuration for CPU chart
    /// </summary>
    public Axis[] CpuXAxes { get; } = CreateTimeAxes();

    /// <summary>
    /// Y-axis configuration for CPU chart (0-100%)
    /// </summary>
    public Axis[] CpuYAxes { get; } = CreatePercentageYAxes("CPU %");

    /// <summary>
    /// X-axis configuration for Memory chart
    /// </summary>
    public Axis[] MemoryXAxes { get; } = CreateTimeAxes();

    /// <summary>
    /// Y-axis configuration for Memory chart (0-100%)
    /// </summary>
    public Axis[] MemoryYAxes { get; } = CreatePercentageYAxes("Memory %");

    public SystemViewModel(
        IUiDispatcher dispatcher,
        ISystemMonitorService systemMonitorService,
        INavigationService navigationService,
        ILogger<SystemViewModel>? logger = null)
    {
        _dispatcher = dispatcher;
        _systemMonitorService = systemMonitorService;
        _navigationService = navigationService;
        _logger = logger;
        _isViewActive = navigationService.CurrentView == Routes.System;

        // Initialize static processor info
        ProcessorName = _systemMonitorService.GetProcessorName();
        ProcessorCount = _systemMonitorService.GetProcessorCount();
        IsCpuTemperatureAvailable = _systemMonitorService.IsCpuTemperatureAvailable;

        // Initialize chart series
        CpuSeries = CreateLineSeries(CpuHistoryPoints, SKColors.DodgerBlue, "CPU");
        MemorySeries = CreateLineSeries(MemoryHistoryPoints, SKColors.MediumPurple, "Memory");

        // Subscribe to stats updates
        _systemMonitorService.StatsUpdated += OnStatsUpdated;

        // Subscribe to navigation changes for view-aware updates
        _navigationService.NavigationChanged += OnNavigationChanged;

        // Get initial stats
        var initialStats = _systemMonitorService.GetCurrentStats();
        UpdateProperties(initialStats);
    }

    private void OnNavigationChanged(string route)
    {
        _isViewActive = route == Routes.System;
    }

    private void OnStatsUpdated(object? sender, SystemStats e)
    {
        // Skip entirely when not visible — no buffering needed for system view
        if (!_isViewActive) return;

        Volatile.Write(ref _pendingSystemStats, e);
        if (Interlocked.Exchange(ref _systemUpdateQueued, 1) == 1) return;

        _dispatcher.Post(() =>
        {
            var pending = _pendingSystemStats;
            Interlocked.Exchange(ref _systemUpdateQueued, 0);
            if (pending != null) UpdateProperties(pending);
        }, UiDispatcherPriority.Background);
    }

    private void UpdateProperties(SystemStats stats)
    {
        // Update CPU properties (cache string formatting)
        CpuUsagePercent = stats.Cpu.UsagePercent;
        if (Math.Abs(_lastCpuPercent - stats.Cpu.UsagePercent) >= 0.05)
        {
            _lastCpuPercent = stats.Cpu.UsagePercent;
            CpuUsageFormatted = $"{stats.Cpu.UsagePercent:F1}%";
        }
        CpuFrequencyMhz = stats.Cpu.FrequencyMhz;
        CpuTemperature = stats.Cpu.TemperatureCelsius;

        // Update per-core usage in-place to avoid N+1 change notifications
        var cores = stats.Cpu.PerCoreUsagePercent;
        if (PerCoreUsage.Count == cores.Length)
        {
            for (var i = 0; i < cores.Length; i++)
                PerCoreUsage[i] = cores[i];
        }
        else
        {
            PerCoreUsage.Clear();
            foreach (var coreUsage in cores)
                PerCoreUsage.Add(coreUsage);
        }

        // Update Memory properties (cache string formatting)
        MemoryUsagePercent = stats.Memory.UsagePercent;
        if (Math.Abs(_lastMemPercent - stats.Memory.UsagePercent) >= 0.05)
        {
            _lastMemPercent = stats.Memory.UsagePercent;
            MemoryUsageFormatted = $"{stats.Memory.UsagePercent:F1}%";
        }
        if (_lastMemUsed != stats.Memory.UsedBytes)
        {
            _lastMemUsed = stats.Memory.UsedBytes;
            MemoryUsed = ByteFormatter.FormatBytes(stats.Memory.UsedBytes);
        }
        if (_lastMemTotal != stats.Memory.TotalBytes)
        {
            _lastMemTotal = stats.Memory.TotalBytes;
            MemoryTotal = ByteFormatter.FormatBytes(stats.Memory.TotalBytes);
        }
        if (_lastMemAvailable != stats.Memory.AvailableBytes)
        {
            _lastMemAvailable = stats.Memory.AvailableBytes;
            MemoryAvailable = ByteFormatter.FormatBytes(stats.Memory.AvailableBytes);
        }

        // Update chart history
        var timestamp = stats.Timestamp;
        AddHistoryPoint(CpuHistoryPoints, timestamp, stats.Cpu.UsagePercent);
        AddHistoryPoint(MemoryHistoryPoints, timestamp, stats.Memory.UsagePercent);
    }

    private void AddHistoryPoint(BatchObservableCollection<DateTimePoint> points, DateTime timestamp, double value)
    {
        points.Add(new DateTimePoint(timestamp, value));

        // Keep only the last MaxHistoryPoints
        ChartCollectionHelper.TrimToMaxCount(points, MaxHistoryPoints);
    }

    private static ISeries[] CreateLineSeries(
        ObservableCollection<DateTimePoint> points,
        SKColor color,
        string name)
    {
        return
        [
            new LineSeries<DateTimePoint>
            {
                Name = name,
                Values = points,
                Fill = new SolidColorPaint(color.WithAlpha(50)),
                Stroke = new SolidColorPaint(color, 2),
                GeometryFill = null,
                GeometryStroke = null,
                GeometrySize = 0,
                LineSmoothness = 0.5,
                AnimationsSpeed = TimeSpan.Zero,
                EnableNullSplitting = false
            }
        ];
    }

    private static Axis[] CreateTimeAxes()
    {
        return
        [
            new DateTimeAxis(TimeSpan.FromSeconds(1), date => date.ToString("HH:mm:ss"))
            {
                Name = null,
                LabelsRotation = 0,
                ShowSeparatorLines = false,
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10
            }
        ];
    }

    private static Axis[] CreatePercentageYAxes(string name)
    {
        return
        [
            new Axis
            {
                Name = name,
                MinLimit = 0,
                MaxLimit = 100,
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 10,
                Labeler = value => $"{value:F0}%"
            }
        ];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _systemMonitorService.StatsUpdated -= OnStatsUpdated;
        _navigationService.NavigationChanged -= OnNavigationChanged;
    }
}

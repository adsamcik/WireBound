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
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// ViewModel for CPU and RAM monitoring
/// </summary>
public sealed partial class SystemViewModel : ObservableObject, IDisposable
{
    private readonly ISystemMonitorService _systemMonitorService;
    private readonly ILogger<SystemViewModel>? _logger;
    private bool _disposed;
    private const int MaxHistoryPoints = 60; // 1 minute of data at 1 point/second

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
    private ObservableCollection<DateTimePoint> _cpuHistoryPoints = [];

    [ObservableProperty]
    private ObservableCollection<DateTimePoint> _memoryHistoryPoints = [];

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
        ISystemMonitorService systemMonitorService,
        ILogger<SystemViewModel>? logger = null)
    {
        _systemMonitorService = systemMonitorService;
        _logger = logger;

        // Initialize static processor info
        ProcessorName = _systemMonitorService.GetProcessorName();
        ProcessorCount = _systemMonitorService.GetProcessorCount();
        IsCpuTemperatureAvailable = _systemMonitorService.IsCpuTemperatureAvailable;

        // Initialize chart series
        CpuSeries = CreateLineSeries(CpuHistoryPoints, SKColors.DodgerBlue, "CPU");
        MemorySeries = CreateLineSeries(MemoryHistoryPoints, SKColors.MediumPurple, "Memory");

        // Subscribe to stats updates
        _systemMonitorService.StatsUpdated += OnStatsUpdated;

        // Get initial stats
        var initialStats = _systemMonitorService.GetCurrentStats();
        UpdateProperties(initialStats);
    }

    private void OnStatsUpdated(object? sender, SystemStats e)
    {
        Dispatcher.UIThread.InvokeAsync(() => UpdateProperties(e));
    }

    private void UpdateProperties(SystemStats stats)
    {
        // Update CPU properties
        CpuUsagePercent = stats.Cpu.UsagePercent;
        CpuUsageFormatted = $"{stats.Cpu.UsagePercent:F1}%";
        CpuFrequencyMhz = stats.Cpu.FrequencyMhz;
        CpuTemperature = stats.Cpu.TemperatureCelsius;

        // Update per-core usage
        PerCoreUsage.Clear();
        foreach (var coreUsage in stats.Cpu.PerCoreUsagePercent)
        {
            PerCoreUsage.Add(coreUsage);
        }

        // Update Memory properties
        MemoryUsagePercent = stats.Memory.UsagePercent;
        MemoryUsageFormatted = $"{stats.Memory.UsagePercent:F1}%";
        MemoryUsed = ByteFormatter.FormatBytes(stats.Memory.UsedBytes);
        MemoryTotal = ByteFormatter.FormatBytes(stats.Memory.TotalBytes);
        MemoryAvailable = ByteFormatter.FormatBytes(stats.Memory.AvailableBytes);

        // Update chart history
        var timestamp = stats.Timestamp;
        AddHistoryPoint(CpuHistoryPoints, timestamp, stats.Cpu.UsagePercent);
        AddHistoryPoint(MemoryHistoryPoints, timestamp, stats.Memory.UsagePercent);
    }

    private void AddHistoryPoint(ObservableCollection<DateTimePoint> points, DateTime timestamp, double value)
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
                LineSmoothness = 0.5
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
    }
}

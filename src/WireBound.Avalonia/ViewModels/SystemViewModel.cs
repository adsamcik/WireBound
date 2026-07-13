using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// Tabs available in the System view.
/// </summary>
public enum SystemTab
{
    LiveMetrics = 0,
    SystemTrends = 1,
}

/// <summary>
/// ViewModel for CPU and RAM monitoring
/// </summary>
public sealed partial class SystemViewModel : ObservableObject, IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ISystemMonitorService _systemMonitorService;
    private readonly INavigationService _navigationService;
    private readonly ISystemSnapshotRepository _systemSnapshotRepository;
    private readonly ISystemHistoryService? _systemHistory;
    private readonly ILogger<SystemViewModel>? _logger;
    private CancellationTokenSource? _historicalLoadCts;
    private bool _disposed;
    private bool _isViewActive;
    private const int MaxHistoryPoints = 60; // 1 minute of data at 1 point/second

    // Buffer for system stats (keeps data even when view is not active)
    private readonly CircularBuffer<(DateTime Timestamp, double CpuPercent, double MemoryPercent, long DiskReadBps, long DiskWriteBps, double DiskActivityPercent)> _statsBuffer = new(3600);

    // Latest-wins coalescing: only one UI post in-flight at a time
    private SystemStats? _pendingSystemStats;
    private int _systemUpdateQueued;

    // Cached formatted values
    private double _lastCpuPercent = -1;
    private double _lastMemPercent = -1;
    private long _lastMemUsed = -1;
    private long _lastMemTotal = -1;
    private long _lastMemAvailable = -1;
    private double _lastDiskActivityPercent = -1;
    private long _lastDiskRead = -1;
    private long _lastDiskWrite = -1;

    // Tab Navigation
    [ObservableProperty]
    private SystemTab _selectedSystemTab = SystemTab.LiveMetrics;

    public bool IsHistoricalTabSelected => SelectedSystemTab != SystemTab.LiveMetrics;

    partial void OnSelectedSystemTabChanged(SystemTab value)
    {
        OnPropertyChanged(nameof(IsHistoricalTabSelected));
        PendingLoadTask = LoadDataForSystemTabAsync(value);
    }

    [RelayCommand]
    private void SelectLiveMetricsTab() => SelectedSystemTab = SystemTab.LiveMetrics;

    [RelayCommand]
    private void SelectSystemTrendsTab() => SelectedSystemTab = SystemTab.SystemTrends;

    // Period Selection for historical system analysis
    [ObservableProperty]
    private InsightsPeriod _selectedPeriod = InsightsPeriod.ThisWeek;

    [ObservableProperty]
    private DateTimeOffset? _customStartDate;

    [ObservableProperty]
    private DateTimeOffset? _customEndDate;

    [ObservableProperty]
    private bool _isCustomPeriod;

    partial void OnSelectedPeriodChanged(InsightsPeriod value)
    {
        IsCustomPeriod = value == InsightsPeriod.Custom;
        if (!IsCustomPeriod && IsHistoricalTabSelected)
        {
            PendingLoadTask = LoadDataForSystemTabAsync(SelectedSystemTab);
        }
    }

    partial void OnCustomStartDateChanged(DateTimeOffset? value)
    {
        if (IsCustomPeriod && IsHistoricalTabSelected && value.HasValue && CustomEndDate.HasValue)
        {
            PendingLoadTask = LoadDataForSystemTabAsync(SelectedSystemTab);
        }
    }

    partial void OnCustomEndDateChanged(DateTimeOffset? value)
    {
        if (IsCustomPeriod && IsHistoricalTabSelected && value.HasValue && CustomStartDate.HasValue)
        {
            PendingLoadTask = LoadDataForSystemTabAsync(SelectedSystemTab);
        }
    }

    [RelayCommand]
    private void SetPeriod(InsightsPeriod period) => SelectedPeriod = period;

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

    // Disk Properties
    [ObservableProperty]
    private double _diskActivityPercent;

    [ObservableProperty]
    private string _diskActivityFormatted = "0%";

    [ObservableProperty]
    private string _diskRead = "0 B/s";

    [ObservableProperty]
    private string _diskWrite = "0 B/s";

    [ObservableProperty]
    private bool _isDiskActivityAvailable;

    // Chart data
    [ObservableProperty]
    private BatchObservableCollection<DateTimePoint> _cpuHistoryPoints = new();

    [ObservableProperty]
    private BatchObservableCollection<DateTimePoint> _memoryHistoryPoints = new();

    [ObservableProperty]
    private BatchObservableCollection<DateTimePoint> _diskReadHistoryPoints = new();

    [ObservableProperty]
    private BatchObservableCollection<DateTimePoint> _diskWriteHistoryPoints = new();

    public ISeries[] CpuSeries { get; }
    public ISeries[] MemorySeries { get; }
    public ISeries[] DiskSeries { get; }

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

    /// <summary>
    /// X-axis configuration for Disk throughput chart
    /// </summary>
    public Axis[] DiskXAxes { get; } = CreateTimeAxes();

    /// <summary>
    /// Y-axis configuration for Disk throughput chart (byte/s, auto-scaled)
    /// </summary>
    public Axis[] DiskYAxes { get; } = CreateBytesYAxes("Disk B/s");

    // Historical System Trends
    [ObservableProperty]
    private double _avgCpuPercent;

    [ObservableProperty]
    private double _maxCpuPercent;

    [ObservableProperty]
    private double _avgMemoryPercent;

    [ObservableProperty]
    private double _maxMemoryPercent;

    [ObservableProperty]
    private double _avgDiskActivityPercent;

    [ObservableProperty]
    private double _maxDiskActivityPercent;

    [ObservableProperty]
    private string _cpuTrendStatus = "Normal";

    [ObservableProperty]
    private string _memoryTrendStatus = "Normal";

    [ObservableProperty]
    private string _diskTrendStatus = "Normal";

    [ObservableProperty]
    private ISeries[] _systemTrendChart = [];

    [ObservableProperty]
    private Axis[] _systemTrendXAxes = CreateHistoricalTimeAxes();

    [ObservableProperty]
    private Axis[] _systemTrendYAxes = CreateHistoricalPercentageYAxes();

    // Historical state
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingOverlay))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingOverlay))]
    private bool _hasData;

    /// <summary>
    /// Show the full-screen loading overlay only on the first load, when there is no
    /// data yet. Reloads triggered by a period/tab change keep the existing data
    /// visible and update in place, so the screen doesn't blink.
    /// </summary>
    public bool ShowLoadingOverlay => IsLoading && !HasData;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public SystemViewModel(
        IUiDispatcher dispatcher,
        ISystemMonitorService systemMonitorService,
        INavigationService navigationService,
        ISystemSnapshotRepository systemSnapshotRepository,
        ILogger<SystemViewModel>? logger = null,
        ISystemHistoryService? systemHistory = null)
    {
        _dispatcher = dispatcher;
        _systemMonitorService = systemMonitorService;
        _navigationService = navigationService;
        _systemSnapshotRepository = systemSnapshotRepository;
        _systemHistory = systemHistory;
        _logger = logger;
        _isViewActive = navigationService.CurrentView == Routes.System;

        // Initialize default dates for historical analysis
        CustomEndDate = DateTimeOffset.Now;
        CustomStartDate = DateTimeOffset.Now.AddDays(-7);

        // Initialize static processor info
        ProcessorName = _systemMonitorService.GetProcessorName();
        ProcessorCount = _systemMonitorService.GetProcessorCount();
        IsCpuTemperatureAvailable = _systemMonitorService.IsCpuTemperatureAvailable;
        IsDiskActivityAvailable = _systemMonitorService.IsDiskActivityAvailable;

        // Initialize chart series
        CpuSeries = CreateLineSeries(CpuHistoryPoints, ChartColors.CpuColor, "CPU");
        MemorySeries = CreateLineSeries(MemoryHistoryPoints, ChartColors.MemoryColor, "Memory");
        DiskSeries = CreateDiskSeries(DiskReadHistoryPoints, DiskWriteHistoryPoints);

        // Subscribe to stats updates
        _systemMonitorService.StatsUpdated += OnStatsUpdated;

        // Subscribe to navigation changes for view-aware updates
        _navigationService.NavigationChanged += OnNavigationChanged;

        // Get initial stats
        var initialStats = _systemMonitorService.GetCurrentStats();
        UpdateProperties(initialStats);
        if (initialStats != null)
        {
            _statsBuffer.Add((initialStats.Timestamp, initialStats.Cpu.UsagePercent, initialStats.Memory.UsagePercent,
                initialStats.Disk.ReadBytesPerSecond, initialStats.Disk.WriteBytesPerSecond, initialStats.Disk.ActivityPercent));
        }

        // Load historical data from database
        InitializationTask = LoadSystemHistoryAsync();
    }

    /// <summary>
    /// Task that completes when historical data has been loaded.
    /// </summary>
    internal Task InitializationTask { get; }

    /// <summary>
    /// Task that completes when the current historical tab load has finished.
    /// </summary>
    internal Task? PendingLoadTask { get; private set; }

    private void OnNavigationChanged(string route)
    {
        var wasActive = _isViewActive;
        _isViewActive = route == Routes.System;

        if (_isViewActive && !wasActive)
        {
            _dispatcher.Post(RefreshChartsFromBuffer);
        }
    }

    private async Task LoadSystemHistoryAsync()
    {
        try
        {
            var since = DateTime.Now.AddHours(-1);
            var history = await _systemSnapshotRepository.GetSystemHistoryAsync(since).ConfigureAwait(false);

            if (history.Count == 0)
                return;

            // Populate the in-memory buffer with historical data
            foreach (var snapshot in history)
            {
                _statsBuffer.Add((snapshot.Timestamp, snapshot.CpuPercent, snapshot.MemoryPercent,
                    snapshot.DiskReadBytesPerSec, snapshot.DiskWriteBytesPerSec, snapshot.DiskActivityPercent));
            }

            // Render chart from buffer on UI thread
            _dispatcher.Post(RefreshChartsFromBuffer);

            _logger?.LogDebug("Loaded {Count} system history snapshots", history.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load system history");
        }
    }

    private void RefreshChartsFromBuffer()
    {
        var bufferData = _statsBuffer.ToArray();
        var startIndex = Math.Max(0, bufferData.Length - MaxHistoryPoints);

        var cpuPoints = new DateTimePoint[bufferData.Length - startIndex];
        var memPoints = new DateTimePoint[bufferData.Length - startIndex];
        var diskReadPoints = new DateTimePoint[bufferData.Length - startIndex];
        var diskWritePoints = new DateTimePoint[bufferData.Length - startIndex];
        for (var i = startIndex; i < bufferData.Length; i++)
        {
            var idx = i - startIndex;
            cpuPoints[idx] = new DateTimePoint(bufferData[i].Timestamp, bufferData[i].CpuPercent);
            memPoints[idx] = new DateTimePoint(bufferData[i].Timestamp, bufferData[i].MemoryPercent);
            diskReadPoints[idx] = new DateTimePoint(bufferData[i].Timestamp, bufferData[i].DiskReadBps);
            diskWritePoints[idx] = new DateTimePoint(bufferData[i].Timestamp, bufferData[i].DiskWriteBps);
        }

        CpuHistoryPoints.ReplaceAll(cpuPoints);
        MemoryHistoryPoints.ReplaceAll(memPoints);
        DiskReadHistoryPoints.ReplaceAll(diskReadPoints);
        DiskWriteHistoryPoints.ReplaceAll(diskWritePoints);
    }

    private void OnStatsUpdated(object? sender, SystemStats e)
    {
        // Always buffer data regardless of view visibility
        _statsBuffer.Add((e.Timestamp, e.Cpu.UsagePercent, e.Memory.UsagePercent,
            e.Disk.ReadBytesPerSecond, e.Disk.WriteBytesPerSecond, e.Disk.ActivityPercent));

        // Skip UI updates when not visible
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

        // Update Disk properties (cache string formatting)
        DiskActivityPercent = stats.Disk.ActivityPercent;
        if (Math.Abs(_lastDiskActivityPercent - stats.Disk.ActivityPercent) >= 0.05)
        {
            _lastDiskActivityPercent = stats.Disk.ActivityPercent;
            DiskActivityFormatted = $"{stats.Disk.ActivityPercent:F1}%";
        }
        if (_lastDiskRead != stats.Disk.ReadBytesPerSecond)
        {
            _lastDiskRead = stats.Disk.ReadBytesPerSecond;
            DiskRead = ByteFormatter.FormatSpeedInBytes(stats.Disk.ReadBytesPerSecond);
        }
        if (_lastDiskWrite != stats.Disk.WriteBytesPerSecond)
        {
            _lastDiskWrite = stats.Disk.WriteBytesPerSecond;
            DiskWrite = ByteFormatter.FormatSpeedInBytes(stats.Disk.WriteBytesPerSecond);
        }

        // Update chart history
        var timestamp = stats.Timestamp;
        AddHistoryPoint(CpuHistoryPoints, timestamp, stats.Cpu.UsagePercent);
        AddHistoryPoint(MemoryHistoryPoints, timestamp, stats.Memory.UsagePercent);
        AddHistoryPoint(DiskReadHistoryPoints, timestamp, stats.Disk.ReadBytesPerSecond);
        AddHistoryPoint(DiskWriteHistoryPoints, timestamp, stats.Disk.WriteBytesPerSecond);
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

    private static ISeries[] CreateDiskSeries(
        ObservableCollection<DateTimePoint> readPoints,
        ObservableCollection<DateTimePoint> writePoints)
    {
        return
        [
            new LineSeries<DateTimePoint>
            {
                Name = "Read",
                Values = readPoints,
                Fill = new SolidColorPaint(ChartColors.DiskReadColor.WithAlpha(40)),
                Stroke = new SolidColorPaint(ChartColors.DiskReadColor, 2),
                GeometryFill = null,
                GeometryStroke = null,
                GeometrySize = 0,
                LineSmoothness = 0.5,
                AnimationsSpeed = TimeSpan.Zero,
                EnableNullSplitting = false
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Write",
                Values = writePoints,
                Fill = new SolidColorPaint(ChartColors.DiskWriteColor.WithAlpha(40)),
                Stroke = new SolidColorPaint(ChartColors.DiskWriteColor, 2),
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
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
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
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                MinLimit = 0,
                MaxLimit = 100,
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                Labeler = value => $"{value:F0}%",
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
            }
        ];
    }

    private static Axis[] CreateBytesYAxes(string name)
    {
        return
        [
            new Axis
            {
                Name = name,
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                MinLimit = 0,
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                Labeler = value => ByteFormatter.FormatSpeedInBytes((long)Math.Max(0, value)),
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
            }
        ];
    }

    private async Task LoadDataForSystemTabAsync(SystemTab tab)
    {
        _historicalLoadCts?.Cancel();

        if (tab == SystemTab.LiveMetrics)
        {
            IsLoading = false;
            HasError = false;
            ErrorMessage = string.Empty;
            return;
        }

        _historicalLoadCts = new CancellationTokenSource();
        var token = _historicalLoadCts.Token;

        try
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;

            var (startDate, endDate) = GetDateRange();

            switch (tab)
            {
                case SystemTab.SystemTrends:
                    await LoadSystemTrendsDataAsync(startDate, endDate, token).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled by a newer tab or period selection.
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading system historical data for tab {Tab}", tab);
            HasError = true;
            ErrorMessage = $"Failed to load data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private (DateOnly Start, DateOnly End) GetDateRange()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return SelectedPeriod switch
        {
            InsightsPeriod.Today => (today, today),
            InsightsPeriod.ThisWeek => (today.AddDays(-7), today),
            InsightsPeriod.ThisMonth => (today.AddMonths(-1), today),
            InsightsPeriod.Custom when CustomStartDate.HasValue && CustomEndDate.HasValue =>
                (DateOnly.FromDateTime(CustomStartDate.Value.DateTime), DateOnly.FromDateTime(CustomEndDate.Value.DateTime)),
            _ => (today.AddDays(-7), today)
        };
    }

    private async Task LoadSystemTrendsDataAsync(DateOnly startDate, DateOnly endDate, CancellationToken token)
    {
        if (_systemHistory is null)
        {
            await UpdateHistoricalUiAsync(() =>
            {
                HasData = false;
                SystemTrendChart = [];
                CpuTrendStatus = "Unavailable";
                MemoryTrendStatus = "Unavailable";
                DiskTrendStatus = "Unavailable";
            }).ConfigureAwait(false);
            return;
        }

        var start = startDate.ToDateTime(TimeOnly.MinValue);
        var end = endDate.ToDateTime(TimeOnly.MaxValue);
        token.ThrowIfCancellationRequested();
        var stats = await _systemHistory.GetHourlyStatsAsync(start, end).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        if (stats.Count == 0)
        {
            await UpdateHistoricalUiAsync(() =>
            {
                HasData = false;
                AvgCpuPercent = 0;
                MaxCpuPercent = 0;
                AvgMemoryPercent = 0;
                MaxMemoryPercent = 0;
                AvgDiskActivityPercent = 0;
                MaxDiskActivityPercent = 0;
                CpuTrendStatus = "No Data";
                MemoryTrendStatus = "No Data";
                DiskTrendStatus = "No Data";
                SystemTrendChart = [];
            }).ConfigureAwait(false);
            return;
        }

        var avgCpu = stats.Average(s => s.AvgCpuPercent);
        var maxCpu = stats.Max(s => s.MaxCpuPercent);
        var avgMemory = stats.Average(s => s.AvgMemoryPercent);
        var maxMemory = stats.Max(s => s.MaxMemoryPercent);
        var avgDisk = stats.Average(s => s.AvgDiskActivityPercent);
        var maxDisk = stats.Max(s => s.MaxDiskActivityPercent);
        var cpuTrendStatus = GetTrendStatus(maxCpu, avgCpu);
        var memoryTrendStatus = GetTrendStatus(maxMemory, avgMemory);
        var diskTrendStatus = GetTrendStatus(maxDisk, avgDisk);
        var chart = CreateSystemTrendSeries(stats);

        await UpdateHistoricalUiAsync(() =>
        {
            AvgCpuPercent = avgCpu;
            MaxCpuPercent = maxCpu;
            AvgMemoryPercent = avgMemory;
            MaxMemoryPercent = maxMemory;
            AvgDiskActivityPercent = avgDisk;
            MaxDiskActivityPercent = maxDisk;
            CpuTrendStatus = cpuTrendStatus;
            MemoryTrendStatus = memoryTrendStatus;
            DiskTrendStatus = diskTrendStatus;
            SystemTrendChart = chart;
            HasData = true;
        }).ConfigureAwait(false);
    }

    private Task UpdateHistoricalUiAsync(Action action) => _dispatcher.InvokeAsync(action);

    private static string GetTrendStatus(double max, double avg)
    {
        if (avg > 80) return "Critical";
        if (avg > 60) return "High";
        if (max > 90) return "Spiky";
        if (avg > 40) return "Moderate";
        return "Normal";
    }

    private static ISeries[] CreateSystemTrendSeries(IReadOnlyList<HourlySystemStats> stats)
    {
        return
        [
            new LineSeries<DateTimePoint>
            {
                Name = "CPU %",
                Values = stats.Select(s => new DateTimePoint(s.Hour, s.AvgCpuPercent)).ToArray(),
                Fill = new SolidColorPaint(ChartColors.CpuColor.WithAlpha(40)),
                Stroke = new SolidColorPaint(ChartColors.CpuColor, 2),
                GeometrySize = 4,
                GeometryFill = new SolidColorPaint(ChartColors.CpuColor),
                GeometryStroke = null,
                LineSmoothness = 0.35
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Memory %",
                Values = stats.Select(s => new DateTimePoint(s.Hour, s.AvgMemoryPercent)).ToArray(),
                Fill = new SolidColorPaint(ChartColors.MemoryColor.WithAlpha(40)),
                Stroke = new SolidColorPaint(ChartColors.MemoryColor, 2),
                GeometrySize = 4,
                GeometryFill = new SolidColorPaint(ChartColors.MemoryColor),
                GeometryStroke = null,
                LineSmoothness = 0.35
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Disk %",
                Values = stats.Select(s => new DateTimePoint(s.Hour, s.AvgDiskActivityPercent)).ToArray(),
                Fill = new SolidColorPaint(ChartColors.DiskColor.WithAlpha(40)),
                Stroke = new SolidColorPaint(ChartColors.DiskColor, 2),
                GeometrySize = 4,
                GeometryFill = new SolidColorPaint(ChartColors.DiskColor),
                GeometryStroke = null,
                LineSmoothness = 0.35
            }
        ];
    }

    private static Axis[] CreateHistoricalTimeAxes()
    {
        return
        [
            new DateTimeAxis(TimeSpan.FromHours(1), date => date.ToString("MM/dd HH:mm"))
            {
                Name = "Time",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 10,
                NameTextSize = 11,
                LabelsRotation = -30,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
            }
        ];
    }

    private static Axis[] CreateHistoricalPercentageYAxes()
    {
        return
        [
            new Axis
            {
                Name = "Percentage",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                NameTextSize = 12,
                MinLimit = 0,
                MaxLimit = 100,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
                Labeler = value => $"{value:F0}%"
            }
        ];
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _historicalLoadCts?.Cancel();
        _historicalLoadCts?.Dispose();
        _systemMonitorService.StatsUpdated -= OnStatsUpdated;
        _navigationService.NavigationChanged -= OnNavigationChanged;
    }
}

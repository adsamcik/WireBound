using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Collections.ObjectModel;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

// ═══════════════════════════════════════════════════════════════════════════════
// HELPER TYPES
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Tabs available in the Insights view
/// </summary>
public enum InsightsTab
{
    NetworkUsage = 0,
    SystemTrends = 1,
    Correlations = 2,
    ResourceInsights = 3
}

/// <summary>
/// Time period options for insights analysis
/// </summary>
public enum InsightsPeriod
{
    Today,
    ThisWeek,
    ThisMonth,
    Custom
}

/// <summary>
/// Represents a data point for the hourly usage heatmap pattern
/// </summary>
/// <param name="Hour">Hour of day (0-23)</param>
/// <param name="DayOfWeek">Day of week (0=Sunday, 6=Saturday)</param>
/// <param name="Value">Usage value for this hour/day combination</param>
public record HourlyPatternItem(int Hour, int DayOfWeek, double Value);

// ═══════════════════════════════════════════════════════════════════════════════
// MAIN VIEWMODEL
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// ViewModel for the unified Insights page that consolidates historical data
/// and provides actionable network and system insights.
/// </summary>
public sealed partial class InsightsViewModel : ObservableObject, IDisposable
{
    private readonly IDataPersistenceService _persistence;
    private readonly ISystemHistoryService? _systemHistory;
    private readonly IResourceInsightsService? _resourceInsights;
    private readonly ILogger<InsightsViewModel>? _logger;
    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    // ═══════════════════════════════════════════════════════════════════════════
    // TAB NAVIGATION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Completes when async initialization finishes. Exposed for testability.</summary>
    public Task InitializationTask { get; }

    /// <summary>
    /// Exposes the last triggered async load for testability.
    /// </summary>
    internal Task? PendingLoadTask { get; private set; }

    [ObservableProperty]
    private InsightsTab _selectedTab = InsightsTab.NetworkUsage;

    partial void OnSelectedTabChanged(InsightsTab value)
    {
        PendingLoadTask = LoadDataForTabAsync(value);
    }

    [RelayCommand]
    private void SelectNetworkTab() => SelectedTab = InsightsTab.NetworkUsage;

    [RelayCommand]
    private void SelectSystemTrendsTab() => SelectedTab = InsightsTab.SystemTrends;

    [RelayCommand]
    private void SelectCorrelationsTab() => SelectedTab = InsightsTab.Correlations;

    [RelayCommand]
    private void SelectResourceInsightsTab() => SelectedTab = InsightsTab.ResourceInsights;

    // ═══════════════════════════════════════════════════════════════════════════
    // PERIOD SELECTION
    // ═══════════════════════════════════════════════════════════════════════════

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
        if (!IsCustomPeriod)
        {
            PendingLoadTask = LoadDataForTabAsync(SelectedTab);
        }
    }

    partial void OnCustomStartDateChanged(DateTimeOffset? value)
    {
        if (IsCustomPeriod && value.HasValue && CustomEndDate.HasValue)
        {
            PendingLoadTask = LoadDataForTabAsync(SelectedTab);
        }
    }

    partial void OnCustomEndDateChanged(DateTimeOffset? value)
    {
        if (IsCustomPeriod && value.HasValue && CustomStartDate.HasValue)
        {
            PendingLoadTask = LoadDataForTabAsync(SelectedTab);
        }
    }

    [RelayCommand]
    private void SetPeriod(InsightsPeriod period)
    {
        SelectedPeriod = period;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // NETWORK USAGE TAB PROPERTIES
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _totalDownload = "0 B";

    [ObservableProperty]
    private string _totalUpload = "0 B";

    [ObservableProperty]
    private string _peakDownloadSpeed = "0 B/s";

    [ObservableProperty]
    private string _peakUploadSpeed = "0 B/s";

    [ObservableProperty]
    private double _downloadChangePercent;

    [ObservableProperty]
    private double _uploadChangePercent;

    [ObservableProperty]
    private ISeries[] _dailyUsageChart = [];

    [ObservableProperty]
    private Axis[] _dailyUsageXAxes = [];

    [ObservableProperty]
    private Axis[] _dailyUsageYAxes = [];

    [ObservableProperty]
    private ObservableCollection<HourlyPatternItem> _hourlyPatternData = [];

    // ═══════════════════════════════════════════════════════════════════════════
    // SYSTEM TRENDS TAB PROPERTIES
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    private double _avgCpuPercent;

    [ObservableProperty]
    private double _maxCpuPercent;

    [ObservableProperty]
    private double _avgMemoryPercent;

    [ObservableProperty]
    private double _maxMemoryPercent;

    [ObservableProperty]
    private string _cpuTrendStatus = "Normal";

    [ObservableProperty]
    private string _memoryTrendStatus = "Normal";

    [ObservableProperty]
    private ISeries[] _systemTrendChart = [];

    [ObservableProperty]
    private Axis[] _systemTrendXAxes = [];

    [ObservableProperty]
    private Axis[] _systemTrendYAxes = [];

    // ═══════════════════════════════════════════════════════════════════════════
    // CORRELATIONS TAB PROPERTIES
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    private double _networkCpuCorrelation;

    [ObservableProperty]
    private double _networkMemoryCorrelation;

    [ObservableProperty]
    private double _cpuMemoryCorrelation;

    [ObservableProperty]
    private ObservableCollection<string> _correlationInsights = [];

    [ObservableProperty]
    private ISeries[] _correlationChart = [];

    // ═══════════════════════════════════════════════════════════════════════════
    // RESOURCE INSIGHTS TAB PROPERTIES
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Whether to show memory (true) or CPU (false) in charts
    /// </summary>
    [ObservableProperty]
    private bool _showMemoryMetric = true;

    partial void OnShowMemoryMetricChanged(bool value)
    {
        if (SelectedTab == InsightsTab.ResourceInsights)
        {
            PendingLoadTask = LoadDataForTabAsync(InsightsTab.ResourceInsights);
        }
    }

    [RelayCommand]
    private void ToggleResourceMetric() => ShowMemoryMetric = !ShowMemoryMetric;

    [ObservableProperty]
    private string _resourceTotalMemoryUsed = "0 B";

    [ObservableProperty]
    private string _resourceTotalCpuPercent = "0%";

    [ObservableProperty]
    private int _resourceAppCount;

    [ObservableProperty]
    private string _resourceLargestConsumer = "—";

    [ObservableProperty]
    private ObservableCollection<AppResourceUsage> _resourceAppList = [];

    [ObservableProperty]
    private ObservableCollection<CategoryResourceUsage> _resourceCategoryList = [];

    [ObservableProperty]
    private ISeries[] _resourceCategoryChart = [];

    [ObservableProperty]
    private ISeries[] _resourceTopAppsChart = [];

    [ObservableProperty]
    private Axis[] _resourceTopAppsXAxes = [];

    [ObservableProperty]
    private Axis[] _resourceTopAppsYAxes = [];

    [ObservableProperty]
    private ISeries[] _resourceHistoryChart = [];

    [ObservableProperty]
    private Axis[] _resourceHistoryXAxes = [];

    [ObservableProperty]
    private Axis[] _resourceHistoryYAxes = [];

    // ═══════════════════════════════════════════════════════════════════════════
    // STATE PROPERTIES
    // ═══════════════════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    // ═══════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════════

    public InsightsViewModel(
        IDataPersistenceService persistence,
        ISystemHistoryService? systemHistory = null,
        IResourceInsightsService? resourceInsights = null,
        ILogger<InsightsViewModel>? logger = null)
    {
        _persistence = persistence;
        _systemHistory = systemHistory;
        _resourceInsights = resourceInsights;
        _logger = logger;

        // Initialize default dates
        CustomEndDate = DateTimeOffset.Now;
        CustomStartDate = DateTimeOffset.Now.AddDays(-7);

        // Initialize axes
        InitializeAxes();

        // Load initial data
        InitializationTask = LoadDataForTabAsync(SelectedTab);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════════════════════════════

    private void InitializeAxes()
    {
        // Daily usage chart axes
        DailyUsageXAxes =
        [
            new Axis
            {
                Name = "Date",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 10,
                NameTextSize = 11,
                LabelsRotation = -45
            }
        ];

        DailyUsageYAxes =
        [
            new Axis
            {
                Name = "Usage",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                NameTextSize = 12,
                MinLimit = 0,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
                Labeler = value => ByteFormatter.FormatBytes((long)value)
            }
        ];

        // System trend chart axes
        SystemTrendXAxes =
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

        SystemTrendYAxes =
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

    // ═══════════════════════════════════════════════════════════════════════════
    // DATA LOADING
    // ═══════════════════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task RefreshAsync()
    {
        HasError = false;
        ErrorMessage = string.Empty;
        var task = LoadDataForTabAsync(SelectedTab);
        PendingLoadTask = task;
        await task;
    }

    private async Task LoadDataForTabAsync(InsightsTab tab)
    {
        // Cancel any previous loading operation
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            IsLoading = true;
            HasError = false;

            var (startDate, endDate) = GetDateRange();

            switch (tab)
            {
                case InsightsTab.NetworkUsage:
                    await LoadNetworkUsageDataAsync(startDate, endDate, token);
                    break;
                case InsightsTab.SystemTrends:
                    await LoadSystemTrendsDataAsync(startDate, endDate, token);
                    break;
                case InsightsTab.Correlations:
                    await LoadCorrelationsDataAsync(startDate, endDate, token);
                    break;
                case InsightsTab.ResourceInsights:
                    await LoadResourceInsightsDataAsync(startDate, endDate, token);
                    break;
            }

        }
        catch (OperationCanceledException)
        {
            // Cancelled, ignore
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading insights data for tab {Tab}", tab);
            HasError = true;
            ErrorMessage = $"Failed to load data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private (DateOnly startDate, DateOnly endDate) GetDateRange()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return SelectedPeriod switch
        {
            InsightsPeriod.Today => (today, today),
            InsightsPeriod.ThisWeek => (today.AddDays(-7), today),
            InsightsPeriod.ThisMonth => (today.AddDays(-30), today),
            InsightsPeriod.Custom when CustomStartDate.HasValue && CustomEndDate.HasValue =>
                (DateOnly.FromDateTime(CustomStartDate.Value.Date),
                 DateOnly.FromDateTime(CustomEndDate.Value.Date)),
            _ => (today.AddDays(-7), today)
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // NETWORK USAGE TAB DATA
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task LoadNetworkUsageDataAsync(DateOnly startDate, DateOnly endDate, CancellationToken token)
    {
        // Get daily usage data
        var dailyData = await _persistence.GetDailyUsageAsync(startDate, endDate);
        token.ThrowIfCancellationRequested();

        if (dailyData.Count == 0)
        {
            TotalDownload = "0 B";
            TotalUpload = "0 B";
            PeakDownloadSpeed = "0 B/s";
            PeakUploadSpeed = "0 B/s";
            DownloadChangePercent = 0;
            UploadChangePercent = 0;
            DailyUsageChart = [];
            HourlyPatternData.Clear();
            HasData = false;
            return;
        }

        // Calculate totals
        var totalReceived = dailyData.Sum(d => d.BytesReceived);
        var totalSent = dailyData.Sum(d => d.BytesSent);
        var peakDown = dailyData.Max(d => d.PeakDownloadSpeed);
        var peakUp = dailyData.Max(d => d.PeakUploadSpeed);

        TotalDownload = ByteFormatter.FormatBytes(totalReceived);
        TotalUpload = ByteFormatter.FormatBytes(totalSent);
        PeakDownloadSpeed = ByteFormatter.FormatSpeed(peakDown);
        PeakUploadSpeed = ByteFormatter.FormatSpeed(peakUp);

        // Calculate change vs previous period
        await CalculatePeriodChangeAsync(startDate, endDate, totalReceived, totalSent, token);

        // Build chart data
        var downloadValues = dailyData.Select(d => d.BytesReceived).ToList();
        var uploadValues = dailyData.Select(d => d.BytesSent).ToList();
        var dateLabels = dailyData.Select(d => d.Date.ToString("MM/dd")).ToArray();

        DailyUsageChart = CreateUsageColumnSeries(downloadValues, uploadValues);
        DailyUsageXAxes =
        [
            new Axis
            {
                Name = "Date",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 10,
                NameTextSize = 11,
                LabelsRotation = -45,
                Labels = dateLabels
            }
        ];

        // Build hourly pattern data
        await LoadHourlyPatternDataAsync(startDate, endDate, token);

        HasData = true;
    }

    private async Task CalculatePeriodChangeAsync(
        DateOnly startDate,
        DateOnly endDate,
        long currentReceived,
        long currentSent,
        CancellationToken token)
    {
        var periodDays = endDate.DayNumber - startDate.DayNumber + 1;
        var previousStart = startDate.AddDays(-periodDays);
        var previousEnd = startDate.AddDays(-1);

        var previousData = await _persistence.GetDailyUsageAsync(previousStart, previousEnd);
        token.ThrowIfCancellationRequested();

        if (previousData.Count == 0)
        {
            DownloadChangePercent = 0;
            UploadChangePercent = 0;
            return;
        }

        var prevReceived = previousData.Sum(d => d.BytesReceived);
        var prevSent = previousData.Sum(d => d.BytesSent);

        DownloadChangePercent = prevReceived > 0
            ? (currentReceived - prevReceived) / (double)prevReceived * 100
            : 0;
        UploadChangePercent = prevSent > 0
            ? (currentSent - prevSent) / (double)prevSent * 100
            : 0;
    }

    private async Task LoadHourlyPatternDataAsync(DateOnly startDate, DateOnly endDate, CancellationToken token)
    {
        HourlyPatternData.Clear();

        // Create aggregated hourly pattern across all days
        var patternDict = new Dictionary<(int Hour, int DayOfWeek), List<long>>();

        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            token.ThrowIfCancellationRequested();
            var hourlyData = await _persistence.GetHourlyUsageAsync(date);
            var dayOfWeek = (int)date.DayOfWeek;

            foreach (var hourData in hourlyData)
            {
                var hour = hourData.Hour.Hour;
                var key = (hour, dayOfWeek);
                if (!patternDict.ContainsKey(key))
                {
                    patternDict[key] = [];
                }
                patternDict[key].Add(hourData.BytesReceived + hourData.BytesSent);
            }
        }

        // Calculate averages and add to collection
        foreach (var kvp in patternDict.OrderBy(k => k.Key.DayOfWeek).ThenBy(k => k.Key.Hour))
        {
            var avgValue = kvp.Value.Count > 0 ? kvp.Value.Average() : 0;
            HourlyPatternData.Add(new HourlyPatternItem(kvp.Key.Hour, kvp.Key.DayOfWeek, avgValue));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // SYSTEM TRENDS TAB DATA
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task LoadSystemTrendsDataAsync(DateOnly startDate, DateOnly endDate, CancellationToken token)
    {
        if (_systemHistory == null)
        {
            // No system history service available
            AvgCpuPercent = 0;
            MaxCpuPercent = 0;
            AvgMemoryPercent = 0;
            MaxMemoryPercent = 0;
            CpuTrendStatus = "Unavailable";
            MemoryTrendStatus = "Unavailable";
            SystemTrendChart = [];
            return;
        }

        // Get historical system data
        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);
        var systemData = await _systemHistory.GetHourlyStatsAsync(startDateTime, endDateTime);
        token.ThrowIfCancellationRequested();

        if (systemData.Count == 0)
        {
            AvgCpuPercent = 0;
            MaxCpuPercent = 0;
            AvgMemoryPercent = 0;
            MaxMemoryPercent = 0;
            CpuTrendStatus = "No Data";
            MemoryTrendStatus = "No Data";
            SystemTrendChart = [];
            HasData = false;
            return;
        }

        // Calculate statistics
        AvgCpuPercent = systemData.Average(s => s.AvgCpuPercent);
        MaxCpuPercent = systemData.Max(s => s.MaxCpuPercent);
        AvgMemoryPercent = systemData.Average(s => s.AvgMemoryPercent);
        MaxMemoryPercent = systemData.Max(s => s.MaxMemoryPercent);

        // Determine trend status
        CpuTrendStatus = GetTrendStatus(AvgCpuPercent, MaxCpuPercent);
        MemoryTrendStatus = GetTrendStatus(AvgMemoryPercent, MaxMemoryPercent);

        // Build chart series
        var cpuPoints = new ObservableCollection<DateTimePoint>(
            systemData.Select(s => new DateTimePoint(s.Hour, s.AvgCpuPercent)));
        var memoryPoints = new ObservableCollection<DateTimePoint>(
            systemData.Select(s => new DateTimePoint(s.Hour, s.AvgMemoryPercent)));

        SystemTrendChart = CreateSystemTrendSeries(cpuPoints, memoryPoints);

        HasData = true;
    }

    private static string GetTrendStatus(double avg, double max)
    {
        return (avg, max) switch
        {
            ( > 80, _) => "Critical",
            ( > 60, _) => "High",
            (_, > 90) => "Spiky",
            ( > 40, _) => "Moderate",
            _ => "Normal"
        };
    }

    private static ISeries[] CreateSystemTrendSeries(
        ObservableCollection<DateTimePoint> cpuPoints,
        ObservableCollection<DateTimePoint> memoryPoints)
    {
        var cpuColor = new SKColor(30, 136, 229);    // Blue for CPU
        var memoryColor = new SKColor(156, 39, 176); // Purple for Memory

        return
        [
            new LineSeries<DateTimePoint>
            {
                Name = "CPU",
                Values = cpuPoints,
                Fill = new LinearGradientPaint(
                    [cpuColor.WithAlpha(60), cpuColor.WithAlpha(0)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1)),
                Stroke = new SolidColorPaint(cpuColor, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Memory",
                Values = memoryPoints,
                Fill = new LinearGradientPaint(
                    [memoryColor.WithAlpha(60), memoryColor.WithAlpha(0)],
                    new SKPoint(0.5f, 0),
                    new SKPoint(0.5f, 1)),
                Stroke = new SolidColorPaint(memoryColor, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5
            }
        ];
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CORRELATIONS TAB DATA
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task LoadCorrelationsDataAsync(DateOnly startDate, DateOnly endDate, CancellationToken token)
    {
        CorrelationInsights.Clear();

        if (_systemHistory == null)
        {
            NetworkCpuCorrelation = 0;
            NetworkMemoryCorrelation = 0;
            CpuMemoryCorrelation = 0;
            CorrelationInsights.Add("System history service unavailable. Install to see correlations.");
            CorrelationChart = [];
            return;
        }

        // Get aligned network and system data
        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue);
        var endDateTime = endDate.ToDateTime(TimeOnly.MaxValue);

        var systemData = await _systemHistory.GetHourlyStatsAsync(startDateTime, endDateTime);
        token.ThrowIfCancellationRequested();

        // Get network data (aggregate by hour to match system data resolution)
        var networkData = new List<(DateTime Time, double Usage)>();
        for (var date = startDate; date <= endDate; date = date.AddDays(1))
        {
            var hourlyData = await _persistence.GetHourlyUsageAsync(date);
            foreach (var h in hourlyData)
            {
                networkData.Add((h.Hour, h.BytesReceived + h.BytesSent));
            }
        }
        token.ThrowIfCancellationRequested();

        if (systemData.Count < 3 || networkData.Count < 3)
        {
            NetworkCpuCorrelation = 0;
            NetworkMemoryCorrelation = 0;
            CpuMemoryCorrelation = 0;
            CorrelationInsights.Add("Insufficient data for correlation analysis.");
            CorrelationChart = [];
            HasData = false;
            return;
        }

        // Align data by hour
        var alignedData = AlignDataByHour(networkData, systemData);

        if (alignedData.Count < 3)
        {
            NetworkCpuCorrelation = 0;
            NetworkMemoryCorrelation = 0;
            CpuMemoryCorrelation = 0;
            CorrelationInsights.Add("Insufficient aligned data for correlation analysis.");
            CorrelationChart = [];
            return;
        }

        // Calculate correlation coefficients
        var networkValues = alignedData.Select(d => d.Network).ToArray();
        var cpuValues = alignedData.Select(d => d.Cpu).ToArray();
        var memoryValues = alignedData.Select(d => d.Memory).ToArray();

        NetworkCpuCorrelation = CalculatePearsonCorrelation(networkValues, cpuValues);
        NetworkMemoryCorrelation = CalculatePearsonCorrelation(networkValues, memoryValues);
        CpuMemoryCorrelation = CalculatePearsonCorrelation(cpuValues, memoryValues);

        // Generate insights
        GenerateCorrelationInsights();

        // Build overlay chart
        BuildCorrelationChart(alignedData);

        HasData = true;
    }

    private static List<(DateTime Time, double Network, double Cpu, double Memory)> AlignDataByHour(
        List<(DateTime Time, double Usage)> networkData,
        IReadOnlyList<HourlySystemStats> systemData)
    {
        var result = new List<(DateTime Time, double Network, double Cpu, double Memory)>();

        // Group network data by hour
        var networkByHour = networkData
            .GroupBy(n => new DateTime(n.Time.Year, n.Time.Month, n.Time.Day, n.Time.Hour, 0, 0))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Usage));

        // Map system data by hour (already hourly aggregates)
        var systemByHour = systemData
            .ToDictionary(s => s.Hour, s => (Cpu: s.AvgCpuPercent, Memory: s.AvgMemoryPercent));

        // Find common hours
        foreach (var hour in networkByHour.Keys.Intersect(systemByHour.Keys).OrderBy(h => h))
        {
            result.Add((hour, networkByHour[hour], systemByHour[hour].Cpu, systemByHour[hour].Memory));
        }

        return result;
    }

    private static double CalculatePearsonCorrelation(double[] x, double[] y)
    {
        if (x.Length != y.Length || x.Length < 2)
            return 0;

        var n = x.Length;
        var sumX = x.Sum();
        var sumY = y.Sum();
        var sumXY = x.Zip(y, (a, b) => a * b).Sum();
        var sumX2 = x.Sum(v => v * v);
        var sumY2 = y.Sum(v => v * v);

        var numerator = n * sumXY - sumX * sumY;
        var denominator = Math.Sqrt((n * sumX2 - sumX * sumX) * (n * sumY2 - sumY * sumY));

        return denominator == 0 ? 0 : numerator / denominator;
    }

    private void GenerateCorrelationInsights()
    {
        // Network-CPU correlation insight
        if (Math.Abs(NetworkCpuCorrelation) > 0.7)
        {
            CorrelationInsights.Add(NetworkCpuCorrelation > 0
                ? "🔗 Strong positive correlation between network activity and CPU usage. Heavy downloads/uploads significantly impact CPU."
                : "🔗 Strong negative correlation between network activity and CPU usage. Network activity tends to occur during low CPU periods.");
        }
        else if (Math.Abs(NetworkCpuCorrelation) > 0.4)
        {
            CorrelationInsights.Add(NetworkCpuCorrelation > 0
                ? "📊 Moderate correlation between network and CPU. Some network tasks affect CPU performance."
                : "📊 Moderate inverse correlation between network and CPU activity.");
        }
        else
        {
            CorrelationInsights.Add("✨ Network activity has minimal impact on CPU usage.");
        }

        // Network-Memory correlation insight
        if (Math.Abs(NetworkMemoryCorrelation) > 0.7)
        {
            CorrelationInsights.Add(NetworkMemoryCorrelation > 0
                ? "🔗 Strong positive correlation between network activity and memory usage. Consider optimizing buffer sizes."
                : "🔗 Network activity inversely correlates with memory usage.");
        }
        else if (Math.Abs(NetworkMemoryCorrelation) > 0.4)
        {
            CorrelationInsights.Add(NetworkMemoryCorrelation > 0
                ? "📊 Moderate correlation between network and memory usage."
                : "📊 Moderate inverse correlation between network and memory.");
        }

        // CPU-Memory correlation insight
        if (Math.Abs(CpuMemoryCorrelation) > 0.7)
        {
            CorrelationInsights.Add(CpuMemoryCorrelation > 0
                ? "🔗 Strong CPU-Memory correlation. Intensive tasks affect both resources simultaneously."
                : "🔗 Unusual: CPU and memory usage are inversely correlated.");
        }

        // Overall system health insight
        if (Math.Abs(NetworkCpuCorrelation) < 0.3 && Math.Abs(NetworkMemoryCorrelation) < 0.3)
        {
            CorrelationInsights.Add("✅ System handles network traffic efficiently with minimal resource impact.");
        }
    }

    private void BuildCorrelationChart(List<(DateTime Time, double Network, double Cpu, double Memory)> alignedData)
    {
        if (alignedData.Count == 0)
        {
            CorrelationChart = [];
            return;
        }

        // Normalize network data to 0-100 scale for comparison
        var maxNetwork = alignedData.Max(d => d.Network);
        var normalizedNetworkPoints = new ObservableCollection<DateTimePoint>(
            alignedData.Select(d => new DateTimePoint(d.Time, maxNetwork > 0 ? d.Network / maxNetwork * 100 : 0)));

        var cpuPoints = new ObservableCollection<DateTimePoint>(
            alignedData.Select(d => new DateTimePoint(d.Time, d.Cpu)));

        var memoryPoints = new ObservableCollection<DateTimePoint>(
            alignedData.Select(d => new DateTimePoint(d.Time, d.Memory)));

        CorrelationChart =
        [
            new LineSeries<DateTimePoint>
            {
                Name = "Network (normalized)",
                Values = normalizedNetworkPoints,
                Fill = null,
                Stroke = new SolidColorPaint(ChartColors.DownloadAccentColor, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5
            },
            new LineSeries<DateTimePoint>
            {
                Name = "CPU %",
                Values = cpuPoints,
                Fill = null,
                Stroke = new SolidColorPaint(new SKColor(30, 136, 229), 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Memory %",
                Values = memoryPoints,
                Fill = null,
                Stroke = new SolidColorPaint(new SKColor(156, 39, 176), 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5
            }
        ];
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // RESOURCE INSIGHTS TAB DATA
    // ═══════════════════════════════════════════════════════════════════════════

    private async Task LoadResourceInsightsDataAsync(DateOnly startDate, DateOnly endDate, CancellationToken token)
    {
        if (_resourceInsights == null)
        {
            ResourceTotalMemoryUsed = "Unavailable";
            ResourceTotalCpuPercent = "Unavailable";
            ResourceAppCount = 0;
            ResourceLargestConsumer = "—";
            ResourceCategoryChart = [];
            ResourceTopAppsChart = [];
            ResourceHistoryChart = [];
            HasData = false;
            return;
        }

        // Get current live data (smoothed)
        // First call establishes CPU baseline; second call (after brief delay) gets real CPU%
        IReadOnlyList<AppResourceUsage> apps;
        IReadOnlyList<CategoryResourceUsage> categories;
        try
        {
            apps = await _resourceInsights.GetCurrentByAppAsync(token);
            token.ThrowIfCancellationRequested();

            // If all CPU values are 0, this is the first poll — do a quick second poll
            if (apps.Count > 0 && apps.All(a => a.CpuPercent == 0))
            {
                await Task.Delay(1500, token);
                apps = await _resourceInsights.GetCurrentByAppAsync(token);
                token.ThrowIfCancellationRequested();
            }

            categories = _resourceInsights.GetCategoryBreakdown(apps);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get current resource data");
            ResourceTotalMemoryUsed = "Error";
            ResourceTotalCpuPercent = "Error";
            ResourceAppCount = 0;
            ResourceLargestConsumer = "—";
            ResourceCategoryChart = [];
            ResourceTopAppsChart = [];
            ResourceHistoryChart = [];
            HasData = false;
            return;
        }

        if (apps.Count == 0)
        {
            ResourceTotalMemoryUsed = "No data";
            ResourceTotalCpuPercent = "0%";
            ResourceAppCount = 0;
            ResourceLargestConsumer = "—";
            ResourceCategoryChart = [];
            ResourceTopAppsChart = [];
            HasData = false;
        }
        else
        {
            // Summary cards
            var totalMemory = apps.Sum(a => a.PrivateBytes);
            var totalCpu = apps.Sum(a => a.CpuPercent);
            ResourceTotalMemoryUsed = ByteFormatter.FormatBytes(totalMemory);
            ResourceTotalCpuPercent = $"{totalCpu:F1}%";
            ResourceAppCount = apps.Count;

            var largest = ShowMemoryMetric
                ? apps.OrderByDescending(a => a.PrivateBytes).First()
                : apps.OrderByDescending(a => a.CpuPercent).First();
            ResourceLargestConsumer = ShowMemoryMetric
                ? $"{largest.AppName} ({largest.PrivateBytesFormatted})"
                : $"{largest.AppName} ({largest.CpuPercentFormatted})";

            // Update app and category lists
            ResourceAppList.Clear();
            foreach (var app in apps.Take(20))
                ResourceAppList.Add(app);

            ResourceCategoryList.Clear();
            foreach (var cat in categories)
                ResourceCategoryList.Add(cat);

            // Category donut chart
            BuildResourceCategoryChart(categories);

            // Top apps bar chart — merge entries with same display name before charting
            var mergedApps = apps
                .GroupBy(a => a.AppName, StringComparer.OrdinalIgnoreCase)
                .Select(g => new AppResourceUsage
                {
                    AppName = g.Key,
                    PrivateBytes = g.Sum(a => a.PrivateBytes),
                    CpuPercent = g.Sum(a => a.CpuPercent),
                    ProcessCount = g.Sum(a => a.ProcessCount)
                })
                .OrderByDescending(a => ShowMemoryMetric ? a.PrivateBytes : (long)(a.CpuPercent * 1_000_000))
                .Take(10)
                .ToList();
            BuildResourceTopAppsChart(mergedApps);

            HasData = true;
        }

        // Historical trend chart — wrapped separately so live data always shows
        try
        {
            var history = await _resourceInsights.GetHistoricalByCategoryAsync(startDate, endDate, token);
            token.ThrowIfCancellationRequested();

            if (history.Count > 0)
            {
                BuildResourceHistoryChart(history);
            }
            else
            {
                ResourceHistoryChart = [];
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load resource history (table may not exist yet)");
            ResourceHistoryChart = [];
        }
    }

    private void BuildResourceCategoryChart(IReadOnlyList<CategoryResourceUsage> categories)
    {
        var pieSeries = new List<ISeries>();
        var colors = new SKColor[]
        {
            new(0, 229, 255),   // Cyan
            new(255, 107, 53),  // Orange
            new(0, 255, 136),   // Green
            new(156, 39, 176),  // Purple
            new(255, 182, 39),  // Amber
            new(30, 136, 229),  // Blue
            new(244, 91, 105),  // Red
            new(160, 168, 184), // Gray
        };

        for (int i = 0; i < categories.Count; i++)
        {
            var cat = categories[i];
            var value = ShowMemoryMetric ? (double)cat.TotalPrivateBytes : cat.TotalCpuPercent;
            if (value <= 0) continue;

            var isMemory = ShowMemoryMetric;
            var color = colors[i % colors.Length];
            pieSeries.Add(new PieSeries<double>
            {
                Name = cat.CategoryName,
                Values = [value],
                Fill = new SolidColorPaint(color),
                Stroke = null,
                Pushout = 0,
                InnerRadius = 60,
                ToolTipLabelFormatter = point =>
                    isMemory
                        ? $"{point.Context.Series.Name}: {ByteFormatter.FormatBytes((long)point.Model)}"
                        : $"{point.Context.Series.Name}: {point.Model:F1}%"
            });
        }

        ResourceCategoryChart = pieSeries.ToArray();
    }

    private void BuildResourceTopAppsChart(IReadOnlyList<AppResourceUsage> apps)
    {
        // Reverse so largest app appears at top of horizontal bar chart
        var sorted = apps.Reverse().ToList();
        var values = ShowMemoryMetric
            ? sorted.Select(a => (double)a.PrivateBytes).ToList()
            : sorted.Select(a => a.CpuPercent).ToList();

        var labels = sorted.Select(a => a.AppName).ToArray();
        var isMemory = ShowMemoryMetric;

        ResourceTopAppsChart =
        [
            new RowSeries<double>
            {
                Name = ShowMemoryMetric ? "Memory" : "CPU",
                Values = values,
                Fill = new SolidColorPaint(ShowMemoryMetric
                    ? new SKColor(156, 39, 176)   // Purple for memory
                    : new SKColor(30, 136, 229)),  // Blue for CPU
                Stroke = null,
                MaxBarWidth = 24,
                Padding = 4,
                YToolTipLabelFormatter = point =>
                {
                    var idx = point.Index;
                    return idx < labels.Length ? labels[idx] : "?";
                },
                XToolTipLabelFormatter = point =>
                    isMemory
                        ? ByteFormatter.FormatBytes((long)point.Model)
                        : $"{point.Model:F1}%"
            }
        ];

        ResourceTopAppsYAxes =
        [
            new Axis
            {
                Labels = labels,
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                MinStep = 1
            }
        ];

        ResourceTopAppsXAxes =
        [
            new Axis
            {
                Name = ShowMemoryMetric ? "Memory" : "CPU %",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 10,
                MinLimit = 0,
                Labeler = ShowMemoryMetric
                    ? (value => ByteFormatter.FormatBytes((long)value))
                    : (value => $"{value:F1}%"),
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
            }
        ];
    }

    private void BuildResourceHistoryChart(IReadOnlyList<ResourceInsightSnapshot> history)
    {
        // Group by category, build one line per top category
        var categoryGroups = history
            .GroupBy(h => h.CategoryName)
            .OrderByDescending(g => ShowMemoryMetric
                ? g.Average(s => s.PrivateBytes)
                : g.Average(s => s.CpuPercent))
            .Take(5)
            .ToList();

        var colors = new SKColor[]
        {
            new(0, 229, 255),
            new(255, 107, 53),
            new(0, 255, 136),
            new(156, 39, 176),
            new(255, 182, 39)
        };

        var series = new List<ISeries>();
        for (int i = 0; i < categoryGroups.Count; i++)
        {
            var group = categoryGroups[i];
            var color = colors[i % colors.Length];
            var points = new ObservableCollection<DateTimePoint>(
                group.Select(s => new DateTimePoint(
                    s.Timestamp,
                    ShowMemoryMetric ? s.PrivateBytes : s.CpuPercent)));

            series.Add(new LineSeries<DateTimePoint>
            {
                Name = group.Key,
                Values = points,
                Fill = null,
                Stroke = new SolidColorPaint(color, 2),
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.5
            });
        }

        ResourceHistoryChart = series.ToArray();

        ResourceHistoryXAxes =
        [
            new DateTimeAxis(TimeSpan.FromHours(1), date => date.ToString("MM/dd HH:mm"))
            {
                Name = "Time",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 10,
                LabelsRotation = -30,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
            }
        ];

        ResourceHistoryYAxes =
        [
            new Axis
            {
                Name = ShowMemoryMetric ? "Memory" : "CPU %",
                NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
                LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
                TextSize = 11,
                MinLimit = 0,
                SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
                Labeler = ShowMemoryMetric
                    ? (value => ByteFormatter.FormatBytes((long)value))
                    : (value => $"{value:F0}%")
            }
        ];
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // CHART HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    private static ISeries[] CreateUsageColumnSeries(
        IReadOnlyCollection<long> downloadValues,
        IReadOnlyCollection<long> uploadValues)
    {
        return
        [
            new StackedColumnSeries<long>
            {
                Name = "Download",
                Values = downloadValues,
                Fill = new SolidColorPaint(ChartColors.DownloadColor),
                Stroke = null,
                MaxBarWidth = 40,
                Padding = 2
            },
            new StackedColumnSeries<long>
            {
                Name = "Upload",
                Values = uploadValues,
                Fill = new SolidColorPaint(ChartColors.UploadColor),
                Stroke = null,
                MaxBarWidth = 40,
                Padding = 2
            }
        ];
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DISPOSAL
    // ═══════════════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }
}

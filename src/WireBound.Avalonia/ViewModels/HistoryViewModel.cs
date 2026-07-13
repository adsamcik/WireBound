using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using WireBound.Core;
using WireBound.Core.Helpers;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Unified historical dashboard: pick a date range and review network usage,
/// system resource trends (CPU/Memory/Disk), and the top apps for that period
/// in a single place. Reuses the existing history services rather than
/// duplicating aggregation logic.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private const int TopAppsLimit = 12;

    private readonly IUiDispatcher _dispatcher;
    private readonly INavigationService _navigationService;
    private readonly ISystemHistoryService _systemHistory;
    private readonly INetworkUsageRepository _networkUsage;
    private readonly IAppOverviewService _appOverview;
    private readonly ILogger<HistoryViewModel>? _logger;

    private CancellationTokenSource? _loadCts;
    private bool _disposed;

    // ── Period selection ────────────────────────────────────────────────────
    [ObservableProperty]
    private InsightsPeriod _selectedPeriod = InsightsPeriod.ThisWeek;

    [ObservableProperty]
    private DateTime? _customStartDate;

    [ObservableProperty]
    private DateTime? _customEndDate;

    [ObservableProperty]
    private bool _isCustomPeriod;

    // ── Summary metrics ─────────────────────────────────────────────────────
    [ObservableProperty]
    private string _totalDownload = "0 B";

    [ObservableProperty]
    private string _totalUpload = "0 B";

    [ObservableProperty]
    private string _rangeLabel = string.Empty;

    [ObservableProperty]
    private double _avgCpuPercent;

    [ObservableProperty]
    private double _avgMemoryPercent;

    [ObservableProperty]
    private double _avgDiskActivityPercent;

    // ── Charts ──────────────────────────────────────────────────────────────
    [ObservableProperty]
    private ISeries[] _networkSeries = [];

    [ObservableProperty]
    private Axis[] _networkXAxes = CreateDayTimeAxes();

    [ObservableProperty]
    private Axis[] _networkYAxes = CreateBytesYAxes();

    [ObservableProperty]
    private ISeries[] _systemSeries = [];

    [ObservableProperty]
    private Axis[] _systemXAxes = CreateHourTimeAxes();

    [ObservableProperty]
    private Axis[] _systemYAxes = CreatePercentageYAxes();

    // ── Top apps ────────────────────────────────────────────────────────────
    [ObservableProperty]
    private ObservableCollection<HistoryAppEntry> _topApps = [];

    // ── State ───────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingOverlay))]
    private bool _isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingOverlay))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>Full-screen spinner only on the very first load (no data yet).</summary>
    public bool ShowLoadingOverlay => IsLoading && !HasData;

    /// <summary>Empty placeholder when a completed load produced nothing.</summary>
    public bool ShowEmptyState => !HasData && !HasError;

    public HistoryViewModel(
        IUiDispatcher dispatcher,
        INavigationService navigationService,
        ISystemHistoryService systemHistory,
        INetworkUsageRepository networkUsage,
        IAppOverviewService appOverview,
        ILogger<HistoryViewModel>? logger = null)
    {
        _dispatcher = dispatcher;
        _navigationService = navigationService;
        _systemHistory = systemHistory;
        _networkUsage = networkUsage;
        _appOverview = appOverview;
        _logger = logger;

        CustomEndDate = DateTime.Now;
        CustomStartDate = DateTime.Now.AddDays(-7);

        InitializationTask = LoadHistoryAsync();
    }

    /// <summary>Completes when the initial history load has finished.</summary>
    internal Task InitializationTask { get; private set; }

    [RelayCommand]
    private void SetPeriod(InsightsPeriod period) => SelectedPeriod = period;

    [RelayCommand]
    private void Refresh() => InitializationTask = LoadHistoryAsync();

    partial void OnSelectedPeriodChanged(InsightsPeriod value)
    {
        IsCustomPeriod = value == InsightsPeriod.Custom;
        if (!IsCustomPeriod)
        {
            InitializationTask = LoadHistoryAsync();
        }
    }

    partial void OnCustomStartDateChanged(DateTime? value)
    {
        if (IsCustomPeriod && value.HasValue && CustomEndDate.HasValue)
        {
            InitializationTask = LoadHistoryAsync();
        }
    }

    partial void OnCustomEndDateChanged(DateTime? value)
    {
        if (IsCustomPeriod && value.HasValue && CustomStartDate.HasValue)
        {
            InitializationTask = LoadHistoryAsync();
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
                (DateOnly.FromDateTime(CustomStartDate.Value), DateOnly.FromDateTime(CustomEndDate.Value)),
            _ => (today.AddDays(-7), today)
        };
    }

    private async Task LoadHistoryAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;

            var (start, end) = GetDateRange();
            if (start > end)
            {
                (start, end) = (end, start);
            }

            var rangeLabel = $"{start:MMM d, yyyy} — {end:MMM d, yyyy}";

            var systemStats = await _systemHistory
                .GetHourlyStatsAsync(start.ToDateTime(TimeOnly.MinValue), end.ToDateTime(TimeOnly.MaxValue))
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            var apps = await _appOverview.GetOverviewAsync(start, end, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            // ── Network usage: hourly buckets for a single day (so "Today" shows
            //    an intraday curve instead of one lonely bar), daily for a range ──
            var isSingleDay = start == end;
            long totalDown = 0;
            long totalUp = 0;
            List<(DateTime Bucket, long Down, long Up)> networkBuckets;

            if (isSingleDay)
            {
                var hourlyUsage = await _networkUsage.GetHourlyUsageAsync(start).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                foreach (var h in hourlyUsage)
                {
                    totalDown += h.BytesReceived;
                    totalUp += h.BytesSent;
                }
                networkBuckets = hourlyUsage
                    .GroupBy(h => h.Hour)
                    .OrderBy(g => g.Key)
                    .Select(g => (
                        Bucket: g.Key,
                        Down: g.Sum(x => x.BytesReceived),
                        Up: g.Sum(x => x.BytesSent)))
                    .ToList();
            }
            else
            {
                var dailyUsage = await _networkUsage.GetDailyUsageAsync(start, end).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                foreach (var d in dailyUsage)
                {
                    totalDown += d.BytesReceived;
                    totalUp += d.BytesSent;
                }
                networkBuckets = dailyUsage
                    .GroupBy(d => d.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => (
                        Bucket: g.Key.ToDateTime(TimeOnly.MinValue),
                        Down: g.Sum(x => x.BytesReceived),
                        Up: g.Sum(x => x.BytesSent)))
                    .ToList();
            }

            var networkSeries = BuildNetworkSeries(networkBuckets, isSingleDay);
            var networkAxes = isSingleDay ? CreateHourOfDayAxes() : CreateDayTimeAxes();

            // ── System trends ───────────────────────────────────────────────
            var avgCpu = systemStats.Count > 0 ? systemStats.Average(s => s.AvgCpuPercent) : 0;
            var avgMem = systemStats.Count > 0 ? systemStats.Average(s => s.AvgMemoryPercent) : 0;
            var avgDisk = systemStats.Count > 0 ? systemStats.Average(s => s.AvgDiskActivityPercent) : 0;
            var systemSeries = BuildSystemSeries(systemStats);

            // ── Top apps (ranked by NETWORK traffic; loopback shown separately) ─
            var topApps = apps
                .Where(a => a.NetworkTotalBytes > 0 || a.LoopbackTotalBytes > 0)
                .OrderByDescending(a => a.NetworkTotalBytes)
                .ThenByDescending(a => a.LoopbackTotalBytes)
                .Take(TopAppsLimit)
                .Select(a => new HistoryAppEntry(
                    string.IsNullOrWhiteSpace(a.AppName) ? a.ProcessName : a.AppName,
                    a.IconPath,
                    a.FormattedNetworkTotalBytes,
                    ByteFormatter.FormatBytes(a.NetworkBytesReceived),
                    ByteFormatter.FormatBytes(a.NetworkBytesSent),
                    a.HasLoopbackTraffic,
                    a.FormattedLocalTotalBytes))
                .ToList();

            var hasData = networkBuckets.Count > 0 || systemStats.Count > 0 || topApps.Count > 0;

            await _dispatcher.InvokeAsync(() =>
            {
                RangeLabel = rangeLabel;
                TotalDownload = ByteFormatter.FormatBytes(totalDown);
                TotalUpload = ByteFormatter.FormatBytes(totalUp);
                AvgCpuPercent = avgCpu;
                AvgMemoryPercent = avgMem;
                AvgDiskActivityPercent = avgDisk;
                NetworkSeries = networkSeries;
                NetworkXAxes = networkAxes;
                SystemSeries = systemSeries;
                TopApps = [.. topApps];
                HasData = hasData;
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer load.
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load history for the selected period");
            await _dispatcher.InvokeAsync(() =>
            {
                HasError = true;
                ErrorMessage = $"Failed to load history: {ex.Message}";
            }).ConfigureAwait(false);
        }
        finally
        {
            await _dispatcher.InvokeAsync(() => IsLoading = false).ConfigureAwait(false);
        }
    }

    private static ISeries[] BuildNetworkSeries(IReadOnlyList<(DateTime Bucket, long Down, long Up)> buckets, bool isSingleDay)
    {
        if (buckets.Count == 0)
            return [];

        var barWidth = isSingleDay ? 12 : 28;

        return
        [
            new ColumnSeries<DateTimePoint>
            {
                Name = "Download",
                Values = buckets.Select(p => new DateTimePoint(p.Bucket, p.Down)).ToArray(),
                Fill = new SolidColorPaint(ChartColors.DownloadColor),
                Stroke = null,
                MaxBarWidth = barWidth,
                IgnoresBarPosition = false
            },
            new ColumnSeries<DateTimePoint>
            {
                Name = "Upload",
                Values = buckets.Select(p => new DateTimePoint(p.Bucket, p.Up)).ToArray(),
                Fill = new SolidColorPaint(ChartColors.UploadColor),
                Stroke = null,
                MaxBarWidth = barWidth,
                IgnoresBarPosition = false
            }
        ];
    }

    private static ISeries[] BuildSystemSeries(IReadOnlyList<Core.Models.HourlySystemStats> stats)
    {
        if (stats.Count == 0)
            return [];

        return
        [
            new LineSeries<DateTimePoint>
            {
                Name = "CPU %",
                Values = stats.Select(s => new DateTimePoint(s.Hour, s.AvgCpuPercent)).ToArray(),
                Fill = new SolidColorPaint(ChartColors.CpuColor.WithAlpha(40)),
                Stroke = new SolidColorPaint(ChartColors.CpuColor, 2),
                GeometrySize = 0,
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.35
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Memory %",
                Values = stats.Select(s => new DateTimePoint(s.Hour, s.AvgMemoryPercent)).ToArray(),
                Fill = new SolidColorPaint(ChartColors.MemoryColor.WithAlpha(40)),
                Stroke = new SolidColorPaint(ChartColors.MemoryColor, 2),
                GeometrySize = 0,
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.35
            },
            new LineSeries<DateTimePoint>
            {
                Name = "Disk %",
                Values = stats.Select(s => new DateTimePoint(s.Hour, s.AvgDiskActivityPercent)).ToArray(),
                Fill = new SolidColorPaint(ChartColors.DiskColor.WithAlpha(40)),
                Stroke = new SolidColorPaint(ChartColors.DiskColor, 2),
                GeometrySize = 0,
                GeometryFill = null,
                GeometryStroke = null,
                LineSmoothness = 0.35
            }
        ];
    }

    private static Axis[] CreateDayTimeAxes() =>
    [
        new DateTimeAxis(TimeSpan.FromDays(1), date => date.ToString("MMM d"))
        {
            Name = null,
            LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
            TextSize = 11,
            LabelsRotation = -30,
            SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
        }
    ];

    private static Axis[] CreateHourOfDayAxes() =>
    [
        new DateTimeAxis(TimeSpan.FromHours(1), date => date.ToString("HH:mm"))
        {
            Name = null,
            LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
            TextSize = 11,
            SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
        }
    ];

    private static Axis[] CreateHourTimeAxes() =>
    [
        new DateTimeAxis(TimeSpan.FromHours(1), date => date.ToString("MM/dd HH:mm"))
        {
            Name = null,
            LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
            TextSize = 10,
            LabelsRotation = -30,
            SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
        }
    ];

    private static Axis[] CreateBytesYAxes() =>
    [
        new Axis
        {
            Name = "Data",
            NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
            MinLimit = 0,
            LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
            TextSize = 11,
            Labeler = value => ByteFormatter.FormatBytes((long)Math.Max(0, value)),
            SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
        }
    ];

    private static Axis[] CreatePercentageYAxes() =>
    [
        new Axis
        {
            Name = "Usage %",
            NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
            MinLimit = 0,
            MaxLimit = 100,
            LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
            TextSize = 11,
            Labeler = value => $"{value:F0}%",
            SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor)
        }
    ];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }
}

/// <summary>
/// A single top-app row in the History dashboard's "Top apps" list. Traffic
/// figures are NETWORK (non-loopback) by default; loopback is surfaced
/// separately via <see cref="LocalFormatted"/> when present.
/// </summary>
public sealed record HistoryAppEntry(
    string Name,
    string? IconPath,
    string TotalFormatted,
    string DownloadFormatted,
    string UploadFormatted,
    bool HasLocal,
    string LocalFormatted)
{
    public bool HasIcon => !string.IsNullOrEmpty(IconPath);
}

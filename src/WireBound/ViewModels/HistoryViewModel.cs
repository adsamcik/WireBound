using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using WireBound.Helpers;
using WireBound.Models;
using WireBound.Services;

namespace WireBound.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    private readonly IDataPersistenceService _persistence;
    private readonly ITelemetryService _telemetry;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private DateOnly _startDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));

    [ObservableProperty]
    private DateOnly _endDate = DateOnly.FromDateTime(DateTime.Today);

    [ObservableProperty]
    private string _totalReceived = "0 B";

    [ObservableProperty]
    private string _totalSent = "0 B";

    [ObservableProperty]
    private string _periodReceived = "0 B";

    [ObservableProperty]
    private string _periodSent = "0 B";

    // Summary statistics
    [ObservableProperty]
    private string _todayReceived = "0 B";

    [ObservableProperty]
    private string _todaySent = "0 B";

    [ObservableProperty]
    private string _weekReceived = "0 B";

    [ObservableProperty]
    private string _weekSent = "0 B";

    [ObservableProperty]
    private string _monthReceived = "0 B";

    [ObservableProperty]
    private string _monthSent = "0 B";

    [ObservableProperty]
    private string _averageDaily = "0 B";

    [ObservableProperty]
    private string _peakDay = "-";

    [ObservableProperty]
    private string _peakDayUsage = "0 B";

    [ObservableProperty]
    private string _mostActiveHour = "-";

    [ObservableProperty]
    private string _mostActiveDay = "-";

    [ObservableProperty]
    private string _trendComparison = "0%";

    [ObservableProperty]
    private bool _isTrendPositive = true;

    // View mode
    [ObservableProperty]
    private int _selectedViewMode = 1; // 0 = Hourly, 1 = Daily, 2 = Weekly

    [ObservableProperty]
    private ObservableCollection<DailyUsage> _dailyUsages = [];

    [ObservableProperty]
    private ObservableCollection<HourlyUsage> _hourlyUsages = [];

    [ObservableProperty]
    private ObservableCollection<WeeklyUsage> _weeklyUsages = [];

    public ISeries[] DailySeries { get; private set; } = [];
    public ISeries[] HourlySeries { get; private set; } = [];
    public ISeries[] WeeklySeries { get; private set; } = [];
    public ISeries[] HourOfDaySeries { get; private set; } = [];
    public ISeries[] DayOfWeekSeries { get; private set; } = [];

    public Axis[] XAxes { get; } =
    [
        new Axis
        {
            Name = "Date",
            NamePaint = new SolidColorPaint(SKColors.Gray),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            LabelsRotation = 45
        }
    ];

    public Axis[] YAxes { get; } =
    [
        new Axis
        {
            Name = "Data Usage",
            NamePaint = new SolidColorPaint(SKColors.Gray),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            MinLimit = 0,
            Labeler = value => ByteFormatter.FormatBytes((long)value)
        }
    ];

    public Axis[] HourlyXAxes { get; } =
    [
        new Axis
        {
            Name = "Hour",
            NamePaint = new SolidColorPaint(SKColors.Gray),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10
        }
    ];

    public Axis[] WeeklyXAxes { get; } =
    [
        new Axis
        {
            Name = "Week",
            NamePaint = new SolidColorPaint(SKColors.Gray),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            LabelsRotation = 45
        }
    ];

    public Axis[] HourOfDayXAxes { get; } =
    [
        new Axis
        {
            Name = "Hour of Day",
            NamePaint = new SolidColorPaint(SKColors.Gray),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            Labels = Enumerable.Range(0, 24).Select(h => $"{h:00}").ToArray()
        }
    ];

    public Axis[] DayOfWeekXAxes { get; } =
    [
        new Axis
        {
            Name = "Day of Week",
            NamePaint = new SolidColorPaint(SKColors.Gray),
            LabelsPaint = new SolidColorPaint(SKColors.Gray),
            TextSize = 10,
            Labels = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"]
        }
    ];

    public HistoryViewModel(IDataPersistenceService persistence, ITelemetryService telemetry)
    {
        _persistence = persistence;
        _telemetry = telemetry;
        _ = LoadDataAsync();
    }

    partial void OnSelectedViewModeChanged(int value)
    {
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadTodayAsync()
    {
        SelectedViewMode = 0; // Hourly view
        StartDate = DateOnly.FromDateTime(DateTime.Today);
        EndDate = DateOnly.FromDateTime(DateTime.Today);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadLast7DaysAsync()
    {
        SelectedViewMode = 1; // Daily view
        StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
        EndDate = DateOnly.FromDateTime(DateTime.Today);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadLast30DaysAsync()
    {
        SelectedViewMode = 1; // Daily view
        StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        EndDate = DateOnly.FromDateTime(DateTime.Today);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadThisMonthAsync()
    {
        SelectedViewMode = 1; // Daily view
        var today = DateTime.Today;
        StartDate = new DateOnly(today.Year, today.Month, 1);
        EndDate = DateOnly.FromDateTime(today);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadLast12WeeksAsync()
    {
        SelectedViewMode = 2; // Weekly view
        StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-84));
        EndDate = DateOnly.FromDateTime(DateTime.Today);
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        // Load summary data
        await LoadSummaryDataAsync();

        // Load data based on view mode
        switch (SelectedViewMode)
        {
            case 0: // Hourly
                await LoadHourlyDataAsync();
                break;
            case 1: // Daily
                await LoadDailyDataAsync();
                break;
            case 2: // Weekly
                await LoadWeeklyDataAsync();
                break;
        }

        // Load analysis charts
        await LoadAnalysisChartsAsync();
    }

    private async Task LoadSummaryDataAsync()
    {
        var (totalReceived, totalSent) = await _persistence.GetTotalUsageAsync();
        TotalReceived = ByteFormatter.FormatBytes(totalReceived);
        TotalSent = ByteFormatter.FormatBytes(totalSent);

        var (todayRecv, todaySnt) = await _persistence.GetTodayUsageAsync();
        TodayReceived = ByteFormatter.FormatBytes(todayRecv);
        TodaySent = ByteFormatter.FormatBytes(todaySnt);

        var (weekRecv, weekSnt) = await _persistence.GetThisWeekUsageAsync();
        WeekReceived = ByteFormatter.FormatBytes(weekRecv);
        WeekSent = ByteFormatter.FormatBytes(weekSnt);

        var (monthRecv, monthSnt) = await _persistence.GetThisMonthUsageAsync();
        MonthReceived = ByteFormatter.FormatBytes(monthRecv);
        MonthSent = ByteFormatter.FormatBytes(monthSnt);

        // Get trend comparison
        try
        {
            var (current, previous) = await _telemetry.GetTrendComparisonAsync(SummaryPeriod.Weekly);
            var changePercent = current.ChangeFromPreviousPeriod;
            TrendComparison = $"{(changePercent >= 0 ? "+" : "")}{changePercent:F1}%";
            IsTrendPositive = changePercent <= 0; // Lower usage is "positive"

            AverageDaily = ByteFormatter.FormatBytes(current.AverageDailyBytes);

            if (current.PeakUsageDate.HasValue)
            {
                PeakDay = current.PeakUsageDate.Value.ToString("MMM dd");
                PeakDayUsage = ByteFormatter.FormatBytes(current.PeakUsageDayBytes);
            }

            MostActiveHour = $"{current.MostActiveHour:00}:00";
            MostActiveDay = current.MostActiveDay.ToString();
        }
        catch
        {
            // Ignore errors in trend calculation
        }
    }

    private async Task LoadHourlyDataAsync()
    {
        var hourlyData = await _persistence.GetHourlyUsageAsync(StartDate);

        HourlyUsages.Clear();
        foreach (var usage in hourlyData)
        {
            HourlyUsages.Add(usage);
        }

        var periodReceived = hourlyData.Sum(h => h.BytesReceived);
        var periodSent = hourlyData.Sum(h => h.BytesSent);
        PeriodReceived = ByteFormatter.FormatBytes(periodReceived);
        PeriodSent = ByteFormatter.FormatBytes(periodSent);

        UpdateHourlyChart(hourlyData);
    }

    private async Task LoadDailyDataAsync()
    {
        var dailyData = await _persistence.GetDailyUsageAsync(StartDate, EndDate);

        DailyUsages.Clear();
        foreach (var usage in dailyData)
        {
            DailyUsages.Add(usage);
        }

        var periodReceived = dailyData.Sum(d => d.BytesReceived);
        var periodSent = dailyData.Sum(d => d.BytesSent);
        PeriodReceived = ByteFormatter.FormatBytes(periodReceived);
        PeriodSent = ByteFormatter.FormatBytes(periodSent);

        UpdateDailyChart(dailyData);
    }

    private async Task LoadWeeklyDataAsync()
    {
        // First, ensure weekly aggregation is up to date
        await _telemetry.AggregateWeeklyDataAsync();

        var weeklyData = await _persistence.GetWeeklyUsageAsync(StartDate, EndDate);

        WeeklyUsages.Clear();
        foreach (var usage in weeklyData)
        {
            WeeklyUsages.Add(usage);
        }

        var periodReceived = weeklyData.Sum(w => w.BytesReceived);
        var periodSent = weeklyData.Sum(w => w.BytesSent);
        PeriodReceived = ByteFormatter.FormatBytes(periodReceived);
        PeriodSent = ByteFormatter.FormatBytes(periodSent);

        UpdateWeeklyChart(weeklyData);
    }

    private async Task LoadAnalysisChartsAsync()
    {
        // Load hour of day distribution
        var hourlyDistribution = await _telemetry.GetUsageByHourOfDayAsync(StartDate, EndDate);
        var hourValues = Enumerable.Range(0, 24).Select(h => (double)(hourlyDistribution.GetValueOrDefault(h, 0))).ToArray();

        HourOfDaySeries =
        [
            new ColumnSeries<double>
            {
                Name = "Average Usage",
                Values = hourValues,
                Fill = new SolidColorPaint(SKColors.Purple),
                MaxBarWidth = 15
            }
        ];
        OnPropertyChanged(nameof(HourOfDaySeries));

        // Load day of week distribution
        var dayDistribution = await _telemetry.GetUsageByDayOfWeekAsync(StartDate, EndDate);
        var dayValues = Enumerable.Range(0, 7)
            .Select(d => (double)(dayDistribution.GetValueOrDefault((DayOfWeek)d, 0)))
            .ToArray();

        DayOfWeekSeries =
        [
            new ColumnSeries<double>
            {
                Name = "Average Usage",
                Values = dayValues,
                Fill = new SolidColorPaint(SKColors.Teal),
                MaxBarWidth = 30
            }
        ];
        OnPropertyChanged(nameof(DayOfWeekSeries));
    }

    private void UpdateHourlyChart(List<HourlyUsage> data)
    {
        var labels = data.Select(h => h.Hour.ToString("HH:mm")).ToArray();
        var downloadValues = data.Select(h => (double)h.BytesReceived).ToArray();
        var uploadValues = data.Select(h => (double)h.BytesSent).ToArray();

        HourlyXAxes[0].Labels = labels;

        HourlySeries =
        [
            new LineSeries<double>
            {
                Name = "Downloaded",
                Values = downloadValues,
                Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 2 },
                GeometrySize = 6,
                GeometryFill = new SolidColorPaint(SKColors.DodgerBlue),
                GeometryStroke = new SolidColorPaint(SKColors.DodgerBlue),
                Fill = null
            },
            new LineSeries<double>
            {
                Name = "Uploaded",
                Values = uploadValues,
                Stroke = new SolidColorPaint(SKColors.LimeGreen) { StrokeThickness = 2 },
                GeometrySize = 6,
                GeometryFill = new SolidColorPaint(SKColors.LimeGreen),
                GeometryStroke = new SolidColorPaint(SKColors.LimeGreen),
                Fill = null
            }
        ];

        OnPropertyChanged(nameof(HourlySeries));
    }

    private void UpdateDailyChart(List<DailyUsage> data)
    {
        var labels = data.Select(d => d.Date.ToString("MM/dd")).ToArray();
        var downloadValues = data.Select(d => (double)d.BytesReceived).ToArray();
        var uploadValues = data.Select(d => (double)d.BytesSent).ToArray();

        XAxes[0].Labels = labels;

        DailySeries =
        [
            new ColumnSeries<double>
            {
                Name = "Downloaded",
                Values = downloadValues,
                Fill = new SolidColorPaint(SKColors.DodgerBlue),
                MaxBarWidth = 20
            },
            new ColumnSeries<double>
            {
                Name = "Uploaded",
                Values = uploadValues,
                Fill = new SolidColorPaint(SKColors.LimeGreen),
                MaxBarWidth = 20
            }
        ];

        OnPropertyChanged(nameof(DailySeries));
    }

    private void UpdateWeeklyChart(List<WeeklyUsage> data)
    {
        var labels = data.Select(w => $"W{w.WeekNumber} ({w.WeekStart:MM/dd})").ToArray();
        var downloadValues = data.Select(w => (double)w.BytesReceived).ToArray();
        var uploadValues = data.Select(w => (double)w.BytesSent).ToArray();

        WeeklyXAxes[0].Labels = labels;

        WeeklySeries =
        [
            new ColumnSeries<double>
            {
                Name = "Downloaded",
                Values = downloadValues,
                Fill = new SolidColorPaint(SKColors.DodgerBlue),
                MaxBarWidth = 35
            },
            new ColumnSeries<double>
            {
                Name = "Uploaded",
                Values = uploadValues,
                Fill = new SolidColorPaint(SKColors.LimeGreen),
                MaxBarWidth = 35
            }
        ];

        OnPropertyChanged(nameof(WeeklySeries));
    }
}

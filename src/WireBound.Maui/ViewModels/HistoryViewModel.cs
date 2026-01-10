using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using WireBound.Maui.Helpers;
using WireBound.Maui.Models;
using WireBound.Maui.Services;

namespace WireBound.Maui.ViewModels;

public partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly IDataPersistenceService _persistence;
    private bool _disposed;

    [ObservableProperty]
    public partial DateTime SelectedDate { get; set; }

    [ObservableProperty]
    public partial DateOnly StartDate { get; set; }

    [ObservableProperty]
    public partial DateOnly EndDate { get; set; }

    [ObservableProperty]
    public partial string TotalReceived { get; set; }

    [ObservableProperty]
    public partial string TotalSent { get; set; }

    [ObservableProperty]
    public partial string PeriodReceived { get; set; }

    [ObservableProperty]
    public partial string PeriodSent { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<DailyUsage> DailyUsages { get; set; }

    [ObservableProperty]
    public partial ISeries[] DailySeries { get; set; }

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

    public HistoryViewModel(IDataPersistenceService persistence)
    {
        _persistence = persistence;

        // Initialize observable properties
        SelectedDate = DateTime.Today;
        StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        EndDate = DateOnly.FromDateTime(DateTime.Today);
        TotalReceived = "0 B";
        TotalSent = "0 B";
        PeriodReceived = "0 B";
        PeriodSent = "0 B";
        DailyUsages = new ObservableCollection<DailyUsage>();
        DailySeries = [];

        // Fire-and-forget with exception handling
        _ = LoadDataAsync().ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"HistoryViewModel.LoadDataAsync failed: {t.Exception.InnerException?.Message}");
            }
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadLast7DaysAsync()
    {
        StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
        EndDate = DateOnly.FromDateTime(DateTime.Today);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadLast30DaysAsync()
    {
        StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        EndDate = DateOnly.FromDateTime(DateTime.Today);
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task LoadThisMonthAsync()
    {
        var today = DateTime.Today;
        StartDate = new DateOnly(today.Year, today.Month, 1);
        EndDate = DateOnly.FromDateTime(today);
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        var dailyData = await _persistence.GetDailyUsageAsync(StartDate, EndDate);
        var (totalReceived, totalSent) = await _persistence.GetTotalUsageAsync();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            DailyUsages.Clear();
            foreach (var usage in dailyData)
            {
                DailyUsages.Add(usage);
            }

            TotalReceived = ByteFormatter.FormatBytes(totalReceived);
            TotalSent = ByteFormatter.FormatBytes(totalSent);

            var periodReceived = dailyData.Sum(d => d.BytesReceived);
            var periodSent = dailyData.Sum(d => d.BytesSent);
            PeriodReceived = ByteFormatter.FormatBytes(periodReceived);
            PeriodSent = ByteFormatter.FormatBytes(periodSent);

            UpdateChart(dailyData);
        });
    }

    private void UpdateChart(List<DailyUsage> data)
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
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Dispose managed resources
                DailyUsages.Clear();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

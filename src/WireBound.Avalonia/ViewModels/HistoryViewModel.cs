using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Collections.ObjectModel;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;
using Microsoft.Data.Sqlite;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Time period options for history view
/// </summary>
public sealed class HistoryPeriodOption
{
    public required string Label { get; init; }
    public required int Days { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// Daily usage item for display with enhanced data
/// </summary>
public sealed class DailyUsageItem
{
    public required DateOnly Date { get; init; }
    public required long BytesReceived { get; init; }
    public required long BytesSent { get; init; }
    public required long PeakDownloadSpeed { get; init; }
    public required long PeakUploadSpeed { get; init; }
    
    /// <summary>
    /// Percentage change from previous day's total (-100 to +∞)
    /// </summary>
    public double? TrendPercent { get; init; }
    
    public string DateFormatted => Date.ToString("ddd, MMM d");
    public string DownloadFormatted => ByteFormatter.FormatBytes(BytesReceived);
    public string UploadFormatted => ByteFormatter.FormatBytes(BytesSent);
    public string TotalFormatted => ByteFormatter.FormatBytes(BytesReceived + BytesSent);
    public string PeakDownloadFormatted => ByteFormatter.FormatSpeed(PeakDownloadSpeed);
    public string PeakUploadFormatted => ByteFormatter.FormatSpeed(PeakUploadSpeed);
    public long TotalBytes => BytesReceived + BytesSent;
    
    public string TrendIndicator => TrendPercent switch
    {
        null => "",
        > 10 => "↑",
        < -10 => "↓",
        _ => "→"
    };
    
    public string TrendColor => TrendPercent switch
    {
        null => "#A0A8B8",
        > 10 => "#00E5FF",  // Download color - more usage
        < -10 => "#00C9A7", // Success color - less usage
        _ => "#A0A8B8"      // Secondary text - stable
    };
    
    public string TrendTooltip => TrendPercent switch
    {
        null => "No previous data",
        > 0 => $"+{TrendPercent:F0}% vs previous day",
        < 0 => $"{TrendPercent:F0}% vs previous day",
        _ => "Same as previous day"
    };
}

/// <summary>
/// Hourly usage item for drill-down view
/// </summary>
public sealed class HourlyUsageItem
{
    public required DateTime Hour { get; init; }
    public required long BytesReceived { get; init; }
    public required long BytesSent { get; init; }
    public required long PeakDownloadSpeed { get; init; }
    public required long PeakUploadSpeed { get; init; }
    
    public string HourFormatted => Hour.ToString("h tt");
    public string DownloadFormatted => ByteFormatter.FormatBytes(BytesReceived);
    public string UploadFormatted => ByteFormatter.FormatBytes(BytesSent);
    public string TotalFormatted => ByteFormatter.FormatBytes(BytesReceived + BytesSent);
    public string PeakDownloadFormatted => ByteFormatter.FormatSpeed(PeakDownloadSpeed);
    public long TotalBytes => BytesReceived + BytesSent;
}

/// <summary>
/// ViewModel for the History page with enhanced analytics
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly IDataPersistenceService _persistence;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _hourlyCts;
    private bool _disposed;

    // === Period Selection ===
    public ObservableCollection<HistoryPeriodOption> PeriodOptions { get; } =
    [
        new() { Label = "7 Days", Days = 7, Description = "Last week" },
        new() { Label = "30 Days", Days = 30, Description = "Last month" },
        new() { Label = "90 Days", Days = 90, Description = "Last 3 months" },
        new() { Label = "Year", Days = 365, Description = "Last 12 months" }
    ];

    [ObservableProperty]
    private HistoryPeriodOption _selectedPeriod;

    // === Summary Statistics ===
    [ObservableProperty]
    private string _totalDownload = "0 B";

    [ObservableProperty]
    private string _totalUpload = "0 B";

    [ObservableProperty]
    private string _totalUsage = "0 B";

    [ObservableProperty]
    private string _dailyAverage = "0 B";

    [ObservableProperty]
    private string _peakDayUsage = "0 B";

    [ObservableProperty]
    private string _peakDayDate = "-";

    [ObservableProperty]
    private string _trendVsPreviousPeriod = "-";

    [ObservableProperty]
    private bool _trendIsPositive;

    // === Chart ===
    [ObservableProperty]
    private ISeries[] _usageSeries = [];

    public Axis[] XAxes { get; private set; }

    public Axis[] YAxes { get; } =
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

    // === Daily Data Table ===
    [ObservableProperty]
    private ObservableCollection<DailyUsageItem> _dailyUsages = [];

    // === Hourly Drill-down ===
    [ObservableProperty]
    private bool _showHourlyPanel;

    [ObservableProperty]
    private DailyUsageItem? _selectedDay;

    [ObservableProperty]
    private ObservableCollection<HourlyUsageItem> _hourlyUsages = [];

    [ObservableProperty]
    private ISeries[] _hourlySeries = [];

    public Axis[] HourlyXAxes { get; } =
    [
        new Axis
        {
            Name = "Hour",
            NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
            LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
            TextSize = 10,
            NameTextSize = 11,
            LabelsRotation = 0
        }
    ];

    public Axis[] HourlyYAxes { get; } =
    [
        new Axis
        {
            Name = "Usage",
            NamePaint = new SolidColorPaint(ChartColors.AxisNameColor),
            LabelsPaint = new SolidColorPaint(ChartColors.AxisLabelColor),
            TextSize = 10,
            NameTextSize = 11,
            MinLimit = 0,
            SeparatorsPaint = new SolidColorPaint(ChartColors.GridLineColor),
            Labeler = value => ByteFormatter.FormatBytes((long)value)
        }
    ];

    // === State ===
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    // === Export State ===
    [ObservableProperty]
    private bool _isExporting;

    [ObservableProperty]
    private bool _exportSuccess;

    [ObservableProperty]
    private string _exportStatusMessage = string.Empty;

    public HistoryViewModel(IDataPersistenceService persistence)
    {
        _persistence = persistence;
        _selectedPeriod = PeriodOptions[1]; // Default to 30 days
        
        XAxes =
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
        
        _ = LoadDataAsync();
    }

    partial void OnSelectedPeriodChanged(HistoryPeriodOption value)
    {
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        HasError = false;
        ErrorMessage = string.Empty;
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task RetryAsync()
    {
        HasError = false;
        ErrorMessage = string.Empty;
        await LoadDataAsync();
    }

    [RelayCommand]
    private void SelectDay(DailyUsageItem? day)
    {
        if (day == null)
        {
            CloseHourlyPanel();
            return;
        }
        
        SelectedDay = day;
        _ = LoadHourlyDataAsync(day.Date);
    }

    [RelayCommand]
    private void CloseHourlyPanel()
    {
        ShowHourlyPanel = false;
        SelectedDay = null;
        HourlyUsages.Clear();
    }

    [RelayCommand]
    private async Task ExportToCsvAsync()
    {
        if (DailyUsages.Count == 0) return;

        IsExporting = true;
        ExportSuccess = false;
        ExportStatusMessage = string.Empty;

        try
        {
            // Get documents path with fallback
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrEmpty(documentsPath) || !Directory.Exists(documentsPath))
            {
                documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            }

            var periodLabel = SelectedPeriod.Days.ToString();
            var fileName = $"WireBound_History_{periodLabel}d_{DateTime.Now:yyyy-MM-dd_HHmmss}.csv";
            var filePath = Path.Combine(documentsPath, fileName);

            var lines = new List<string>
            {
                $"# WireBound Network History Export",
                $"# Period: {SelectedPeriod.Label} ({SelectedPeriod.Description})",
                $"# Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"# Total Downloaded: {TotalDownload}",
                $"# Total Uploaded: {TotalUpload}",
                "",
                "Date,Downloaded (bytes),Uploaded (bytes),Total (bytes),Peak Download (bps),Peak Upload (bps)"
            };

            foreach (var day in DailyUsages.OrderBy(d => d.Date))
            {
                lines.Add($"{day.Date:yyyy-MM-dd},{day.BytesReceived},{day.BytesSent},{day.TotalBytes},{day.PeakDownloadSpeed},{day.PeakUploadSpeed}");
            }

            await File.WriteAllLinesAsync(filePath, lines);
            
            ExportSuccess = true;
            ExportStatusMessage = $"Exported to {fileName}";
            
            // Try to open the file location
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch
            {
                // Explorer failed but export succeeded - still show success
            }

            // Clear success message after delay
            _ = ClearExportStatusAsync();
        }
        catch (UnauthorizedAccessException)
        {
            ExportStatusMessage = "Permission denied. Cannot write to folder.";
        }
        catch (IOException ex)
        {
            ExportStatusMessage = $"Write error: {ex.Message}";
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private async Task ClearExportStatusAsync()
    {
        await Task.Delay(5000);
        ExportStatusMessage = string.Empty;
        ExportSuccess = false;
    }

    private async Task LoadDataAsync()
    {
        // Cancel any pending load operation
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        IsLoading = true;
        HasError = false;

        try
        {
            var endDate = DateOnly.FromDateTime(DateTime.Today);
            var startDate = endDate.AddDays(-SelectedPeriod.Days);
            var previousPeriodStart = startDate.AddDays(-SelectedPeriod.Days);

            // Load current and previous period data in parallel
            var usagesTask = _persistence.GetDailyUsageAsync(startDate, endDate);
            var previousUsagesTask = _persistence.GetDailyUsageAsync(previousPeriodStart, startDate.AddDays(-1));
            
            await Task.WhenAll(usagesTask, previousUsagesTask);
            
            ct.ThrowIfCancellationRequested();
            
            var usages = await usagesTask;
            var previousUsages = await previousUsagesTask;

            // Build daily items with trends
            DailyUsages.Clear();
            DailyUsageItem? previousItem = null;
            
            foreach (var usage in usages.OrderByDescending(u => u.Date))
            {
                var currentTotal = usage.BytesReceived + usage.BytesSent;
                double? trendPercent = null;
                
                if (previousItem != null && previousItem.TotalBytes > 0)
                {
                    trendPercent = ((double)currentTotal / previousItem.TotalBytes - 1) * 100;
                }

                var item = new DailyUsageItem
                {
                    Date = usage.Date,
                    BytesReceived = usage.BytesReceived,
                    BytesSent = usage.BytesSent,
                    PeakDownloadSpeed = usage.PeakDownloadSpeed,
                    PeakUploadSpeed = usage.PeakUploadSpeed,
                    TrendPercent = trendPercent
                };
                
                DailyUsages.Add(item);
                previousItem = item;
            }

            HasData = DailyUsages.Count > 0;

            // Calculate summary statistics
            if (HasData)
            {
                var totalDown = usages.Sum(u => u.BytesReceived);
                var totalUp = usages.Sum(u => u.BytesSent);
                var total = totalDown + totalUp;
                var activeDays = usages.Count;
                
                TotalDownload = ByteFormatter.FormatBytes(totalDown);
                TotalUpload = ByteFormatter.FormatBytes(totalUp);
                TotalUsage = ByteFormatter.FormatBytes(total);
                DailyAverage = activeDays > 0 ? ByteFormatter.FormatBytes(total / activeDays) : "0 B";

                var peakDay = usages.OrderByDescending(u => u.BytesReceived + u.BytesSent).FirstOrDefault();
                if (peakDay != null)
                {
                    PeakDayUsage = ByteFormatter.FormatBytes(peakDay.BytesReceived + peakDay.BytesSent);
                    PeakDayDate = peakDay.Date.ToString("MMM d");
                }

                // Calculate trend vs previous period
                var previousTotal = previousUsages.Sum(u => u.BytesReceived + u.BytesSent);
                if (previousTotal > 0)
                {
                    var trendPercent = ((double)total / previousTotal - 1) * 100;
                    TrendIsPositive = trendPercent >= 0;
                    TrendVsPreviousPeriod = trendPercent >= 0 
                        ? $"+{trendPercent:F0}%" 
                        : $"{trendPercent:F0}%";
                }
                else
                {
                    TrendVsPreviousPeriod = "-";
                }

                // Build chart data
                BuildUsageChart(usages.OrderBy(u => u.Date).ToList());
            }
            else
            {
                ResetSummary();
            }
        }
        catch (OperationCanceledException)
        {
            // Load was cancelled, ignore
        }
        catch (SqliteException ex)
        {
            HasError = true;
            ErrorMessage = $"Database error: {ex.Message}";
            ResetSummary();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to load history: {ex.Message}";
            ResetSummary();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ResetSummary()
    {
        TotalDownload = "0 B";
        TotalUpload = "0 B";
        TotalUsage = "0 B";
        DailyAverage = "0 B";
        PeakDayUsage = "0 B";
        PeakDayDate = "-";
        TrendVsPreviousPeriod = "-";
        UsageSeries = [];
    }

    private void BuildUsageChart(List<DailyUsage> usages)
    {
        var downloadColor = ChartColors.DownloadColor;
        var uploadColor = ChartColors.UploadColor;

        var labels = usages.Select(u => u.Date.ToString("M/d")).ToArray();
        
        // Update X axis labels
        XAxes[0].Labels = labels;

        UsageSeries =
        [
            new ColumnSeries<long>
            {
                Name = "Download",
                Values = usages.Select(u => u.BytesReceived).ToArray(),
                Fill = new SolidColorPaint(downloadColor),
                Stroke = null,
                MaxBarWidth = 20,
                Padding = 2
            },
            new ColumnSeries<long>
            {
                Name = "Upload",
                Values = usages.Select(u => u.BytesSent).ToArray(),
                Fill = new SolidColorPaint(uploadColor),
                Stroke = null,
                MaxBarWidth = 20,
                Padding = 2
            }
        ];
    }

    private async Task LoadHourlyDataAsync(DateOnly date)
    {
        // Cancel any pending hourly load
        _hourlyCts?.Cancel();
        _hourlyCts = new CancellationTokenSource();
        var ct = _hourlyCts.Token;

        try
        {
            var hourlyData = await _persistence.GetHourlyUsageAsync(date);
            
            ct.ThrowIfCancellationRequested();
        
        HourlyUsages.Clear();
        foreach (var hour in hourlyData.OrderBy(h => h.Hour))
        {
            HourlyUsages.Add(new HourlyUsageItem
            {
                Hour = hour.Hour,
                BytesReceived = hour.BytesReceived,
                BytesSent = hour.BytesSent,
                PeakDownloadSpeed = hour.PeakDownloadSpeed,
                PeakUploadSpeed = hour.PeakUploadSpeed
            });
        }

            // Build hourly chart
            if (HourlyUsages.Count > 0)
            {
                BuildHourlyChart(hourlyData.OrderBy(h => h.Hour).ToList());
            }

            ShowHourlyPanel = true;
        }
        catch (OperationCanceledException)
        {
            // Cancelled, ignore
        }
        catch
        {
            // Failed to load hourly data - panel will show empty
            ShowHourlyPanel = true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _hourlyCts?.Cancel();
        _hourlyCts?.Dispose();
    }

    private void BuildHourlyChart(List<HourlyUsage> hourlyData)
    {
        var downloadColor = ChartColors.DownloadColor;
        var uploadColor = ChartColors.UploadColor;

        var labels = hourlyData.Select(h => h.Hour.ToString("h tt")).ToArray();
        HourlyXAxes[0].Labels = labels;

        HourlySeries =
        [
            new ColumnSeries<long>
            {
                Name = "Download",
                Values = hourlyData.Select(h => h.BytesReceived).ToArray(),
                Fill = new SolidColorPaint(downloadColor),
                Stroke = null,
                MaxBarWidth = 16,
                Padding = 1
            },
            new ColumnSeries<long>
            {
                Name = "Upload",
                Values = hourlyData.Select(h => h.BytesSent).ToArray(),
                Fill = new SolidColorPaint(uploadColor),
                Stroke = null,
                MaxBarWidth = 16,
                Padding = 1
            }
        ];
    }
}

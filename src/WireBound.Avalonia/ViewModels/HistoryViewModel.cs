using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Daily usage item for display
/// </summary>
public sealed class DailyUsageItem
{
    public required DateOnly Date { get; init; }
    public required long BytesReceived { get; init; }
    public required long BytesSent { get; init; }
    
    public string DateFormatted => Date.ToString("ddd, MMM d");
    public string DownloadFormatted => ByteFormatter.FormatBytes(BytesReceived);
    public string UploadFormatted => ByteFormatter.FormatBytes(BytesSent);
    public string TotalFormatted => ByteFormatter.FormatBytes(BytesReceived + BytesSent);
}

/// <summary>
/// ViewModel for the History page
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IDataPersistenceService _persistence;

    [ObservableProperty]
    private ObservableCollection<DailyUsageItem> _dailyUsages = [];

    [ObservableProperty]
    private bool _isLoading;

    public HistoryViewModel(IDataPersistenceService persistence)
    {
        _persistence = persistence;
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        IsLoading = true;

        try
        {
            var endDate = DateOnly.FromDateTime(DateTime.Today);
            var startDate = endDate.AddDays(-30);

            var usages = await _persistence.GetDailyUsageAsync(startDate, endDate);

            DailyUsages.Clear();
            foreach (var usage in usages)
            {
                DailyUsages.Add(new DailyUsageItem
                {
                    Date = usage.Date,
                    BytesReceived = usage.BytesReceived,
                    BytesSent = usage.BytesSent
                });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}

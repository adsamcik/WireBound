using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WireBound.Helpers;
using WireBound.Models;
using WireBound.Services;

namespace WireBound.ViewModels;

public partial class ApplicationsViewModel : ObservableObject, IDisposable
{
    private readonly IDataPersistenceService _persistence;
    private readonly IProcessNetworkService? _processService;
    private bool _disposed;

    [ObservableProperty]
    private ObservableCollection<AppUsageRecord> _allApps = [];

    [ObservableProperty]
    private ObservableCollection<ProcessNetworkStats> _activeApps = [];

    [ObservableProperty]
    private DateOnly _startDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));

    [ObservableProperty]
    private DateOnly _endDate = DateOnly.FromDateTime(DateTime.Today);

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedGroupBy = "Process Name";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private ObservableCollection<AppUsageRecord> _filteredApps = [];

    [ObservableProperty]
    private string _totalDownload = "0 B";

    [ObservableProperty]
    private string _totalUpload = "0 B";

    [ObservableProperty]
    private int _appCount;

    public string[] GroupByOptions { get; } = ["Process Name", "Executable Path"];

    public ApplicationsViewModel(
        IDataPersistenceService persistence,
        IProcessNetworkService? processService = null)
    {
        _persistence = persistence;
        _processService = processService;

        if (_processService is not null)
        {
            _processService.StatsUpdated += OnProcessStatsUpdated;
        }

        // Load data on init
        _ = LoadDataAsync();
    }

    private void OnProcessStatsUpdated(object? sender, ProcessStatsUpdatedEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            ActiveApps.Clear();
            foreach (var stat in e.Stats.OrderByDescending(s => s.TotalSpeedBps).Take(20))
            {
                ActiveApps.Add(stat);
            }
        });
    }

    partial void OnStartDateChanged(DateOnly value) => _ = LoadDataAsync();

    partial void OnEndDateChanged(DateOnly value) => _ = LoadDataAsync();

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    partial void OnSelectedGroupByChanged(string value) => ApplyFilters();

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        try
        {
            IsLoading = true;

            var records = await _persistence.GetAllAppUsageAsync(StartDate, EndDate, UsageGranularity.Daily);
            
            AllApps.Clear();
            foreach (var record in records.OrderByDescending(r => r.TotalBytes))
            {
                AllApps.Add(record);
            }

            ApplyFilters();
            UpdateSummary();

            // Load active apps if service is available
            if (_processService?.IsRunning == true)
            {
                var activeStats = _processService.GetCurrentStats();
                ActiveApps.Clear();
                foreach (var stat in activeStats.OrderByDescending(s => s.TotalSpeedBps).Take(20))
                {
                    ActiveApps.Add(stat);
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDataAsync();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        EndDate = DateOnly.FromDateTime(DateTime.Today);
        SelectedGroupBy = "Process Name";
    }

    private void ApplyFilters()
    {
        var filtered = AllApps.AsEnumerable();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            filtered = filtered.Where(a => 
                a.AppName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                a.ProcessName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                a.ExecutablePath.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        // Group by selected option
        IEnumerable<AppUsageRecord> grouped;
        if (SelectedGroupBy == "Executable Path")
        {
            grouped = filtered
                .GroupBy(a => a.ExecutablePath)
                .Select(g => new AppUsageRecord
                {
                    AppName = g.First().AppName,
                    ProcessName = g.First().ProcessName,
                    ExecutablePath = g.Key,
                    AppIdentifier = g.First().AppIdentifier,
                    BytesReceived = g.Sum(x => x.BytesReceived),
                    BytesSent = g.Sum(x => x.BytesSent),
                    Timestamp = g.Max(x => x.Timestamp),
                    Granularity = UsageGranularity.Daily
                })
                .OrderByDescending(a => a.TotalBytes);
        }
        else
        {
            grouped = filtered
                .GroupBy(a => a.ProcessName)
                .Select(g => new AppUsageRecord
                {
                    AppName = g.First().AppName,
                    ProcessName = g.Key,
                    ExecutablePath = g.First().ExecutablePath,
                    AppIdentifier = g.First().AppIdentifier,
                    BytesReceived = g.Sum(x => x.BytesReceived),
                    BytesSent = g.Sum(x => x.BytesSent),
                    Timestamp = g.Max(x => x.Timestamp),
                    Granularity = UsageGranularity.Daily
                })
                .OrderByDescending(a => a.TotalBytes);
        }

        FilteredApps.Clear();
        foreach (var app in grouped)
        {
            FilteredApps.Add(app);
        }

        AppCount = FilteredApps.Count;
    }

    private void UpdateSummary()
    {
        var totalDown = AllApps.Sum(a => a.BytesReceived);
        var totalUp = AllApps.Sum(a => a.BytesSent);

        TotalDownload = ByteFormatter.Format(totalDown);
        TotalUpload = ByteFormatter.Format(totalUp);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            if (_processService is not null)
            {
                _processService.StatsUpdated -= OnProcessStatsUpdated;
            }
        }

        _disposed = true;
    }
}

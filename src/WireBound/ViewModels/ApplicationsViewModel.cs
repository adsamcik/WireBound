using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WireBound.Helpers;
using WireBound.Models;
using WireBound.Services;

namespace WireBound.ViewModels;

public sealed partial class ApplicationsViewModel : ObservableObject, IDisposable
{
    private readonly IDataPersistenceService _dataPersistence;
    private readonly IProcessNetworkService? _processNetworkService;
    private readonly IElevationService _elevationService;
    private bool _disposed;

    [ObservableProperty]
    public partial ObservableCollection<AppUsageRecord> AllApps { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<ProcessNetworkStats> ActiveApps { get; set; }

    [ObservableProperty]
    public partial DateTime StartDate { get; set; }

    [ObservableProperty]
    public partial DateTime EndDate { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsPlatformSupported { get; set; }

    [ObservableProperty]
    public partial bool IsPerAppTrackingEnabled { get; set; }

    [ObservableProperty]
    public partial string TotalDownload { get; set; }

    [ObservableProperty]
    public partial string TotalUpload { get; set; }

    [ObservableProperty]
    public partial int AppCount { get; set; }

    [ObservableProperty]
    public partial bool RequiresElevation { get; set; }

    [ObservableProperty]
    public partial bool IsElevated { get; set; }

    [ObservableProperty]
    public partial bool IsRequestingElevation { get; set; }

    public ApplicationsViewModel(
        IDataPersistenceService dataPersistence,
        IElevationService elevationService,
        IProcessNetworkService? processNetworkService = null)
    {
        _dataPersistence = dataPersistence;
        _elevationService = elevationService;
        _processNetworkService = processNetworkService;

        AllApps = new ObservableCollection<AppUsageRecord>();
        ActiveApps = new ObservableCollection<ProcessNetworkStats>();
        SearchText = string.Empty;
        TotalDownload = "0 B";
        TotalUpload = "0 B";

        // Default date range: last 7 days
        EndDate = DateTime.Today;
        StartDate = DateTime.Today.AddDays(-7);

        IsPlatformSupported = _processNetworkService?.IsPlatformSupported ?? false;
        IsElevated = _elevationService.IsElevated;
        RequiresElevation = _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring);

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        await LoadSettingsAsync();
        await LoadDataAsync();
        SubscribeToProcessEvents();
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            var settings = await _dataPersistence.GetSettingsAsync();
            IsPerAppTrackingEnabled = settings.IsPerAppTrackingEnabled;
        }
        catch
        {
            IsPerAppTrackingEnabled = false;
        }
    }

    private void SubscribeToProcessEvents()
    {
        if (_processNetworkService != null && IsPerAppTrackingEnabled)
        {
            _processNetworkService.StatsUpdated += OnProcessStatsUpdated;
        }
    }

    private void OnProcessStatsUpdated(object? sender, ProcessStatsUpdatedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ActiveApps.Clear();
            var topApps = e.Stats
                .OrderByDescending(s => s.TotalSpeedBps)
                .Take(10);
            
            foreach (var app in topApps)
            {
                ActiveApps.Add(app);
            }
        });
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        if (IsLoading) return;

        try
        {
            IsLoading = true;

            var startDate = DateOnly.FromDateTime(StartDate);
            var endDate = DateOnly.FromDateTime(EndDate);

            var apps = await _dataPersistence.GetAllAppUsageAsync(startDate, endDate);

            // Apply search filter with length limit for security
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var sanitizedSearch = SearchText.Length > 256 ? SearchText[..256] : SearchText;
                apps = apps
                    .Where(a => a.AppName.Contains(sanitizedSearch, StringComparison.OrdinalIgnoreCase) ||
                               a.ProcessName.Contains(sanitizedSearch, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Group by app identifier and sum totals
            var groupedApps = apps
                .GroupBy(a => a.AppIdentifier)
                .Select(g => new AppUsageRecord
                {
                    AppIdentifier = g.Key,
                    AppName = g.First().AppName,
                    ProcessName = g.First().ProcessName,
                    ExecutablePath = g.First().ExecutablePath,
                    BytesReceived = g.Sum(a => a.BytesReceived),
                    BytesSent = g.Sum(a => a.BytesSent),
                    PeakDownloadSpeed = g.Max(a => a.PeakDownloadSpeed),
                    PeakUploadSpeed = g.Max(a => a.PeakUploadSpeed),
                    LastUpdated = g.Max(a => a.LastUpdated)
                })
                .OrderByDescending(a => a.TotalBytes)
                .ToList();

            AllApps.Clear();
            foreach (var app in groupedApps)
            {
                AllApps.Add(app);
            }

            // Update summary
            AppCount = groupedApps.Count;
            TotalDownload = ByteFormatter.FormatBytes(groupedApps.Sum(a => a.BytesReceived));
            TotalUpload = ByteFormatter.FormatBytes(groupedApps.Sum(a => a.BytesSent));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading app data: {ex.Message}");
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
        StartDate = DateTime.Today.AddDays(-7);
        EndDate = DateTime.Today;
        _ = LoadDataAsync();
    }

    [RelayCommand]
    private async Task RequestElevationAsync()
    {
        if (IsRequestingElevation || IsElevated)
        {
            return;
        }

        try
        {
            IsRequestingElevation = true;
            
            // Request elevation - this will restart the app if successful
            var elevated = await _elevationService.RequestElevationAsync();
            
            if (!elevated)
            {
                // User cancelled or elevation failed - update state to reflect we're still not elevated
                RequiresElevation = _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring);
            }
            // If elevated is true, the app is restarting so we won't get here
        }
        finally
        {
            IsRequestingElevation = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = LoadDataAsync();
    }

    partial void OnStartDateChanged(DateTime value)
    {
        _ = LoadDataAsync();
    }

    partial void OnEndDateChanged(DateTime value)
    {
        _ = LoadDataAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_processNetworkService != null)
        {
            _processNetworkService.StatsUpdated -= OnProcessStatsUpdated;
        }
    }
}

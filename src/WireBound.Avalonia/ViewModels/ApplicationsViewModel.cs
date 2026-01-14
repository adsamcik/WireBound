using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Applications page
/// </summary>
public sealed partial class ApplicationsViewModel : ObservableObject
{
    private readonly IDataPersistenceService _persistence;
    private readonly IProcessNetworkService? _processNetworkService;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isPlatformSupported = true;

    [ObservableProperty]
    private bool _isPerAppTrackingEnabled;

    [ObservableProperty]
    private bool _requiresElevation;

    [ObservableProperty]
    private bool _isRequestingElevation;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private DateTime? _startDate = DateTime.Now.AddDays(-7);

    [ObservableProperty]
    private DateTime? _endDate = DateTime.Now;

    [ObservableProperty]
    private int _appCount;

    [ObservableProperty]
    private string _totalDownload = "0 B";

    [ObservableProperty]
    private string _totalUpload = "0 B";

    [ObservableProperty]
    private ObservableCollection<ProcessNetworkStats> _activeApps = [];

    [ObservableProperty]
    private ObservableCollection<AppUsageRecord> _allApps = [];

    public ApplicationsViewModel(
        IDataPersistenceService persistence,
        IServiceProvider serviceProvider)
    {
        _persistence = persistence;
        _processNetworkService = serviceProvider.GetService(typeof(IProcessNetworkService)) as IProcessNetworkService;

        // Per-app network tracking requires IProcessNetworkService which is not yet implemented cross-platform
        IsPlatformSupported = _processNetworkService != null;
        IsPerAppTrackingEnabled = _processNetworkService?.IsRunning == true;
        RequiresElevation = false;

        if (_processNetworkService != null)
        {
            _processNetworkService.StatsUpdated += OnProcessStatsUpdated;
        }

        _ = LoadDataAsync();
    }

    private void OnProcessStatsUpdated(object? sender, ProcessStatsUpdatedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ActiveApps.Clear();
            foreach (var stats in e.Stats.OrderByDescending(s => s.TotalSpeedBps).Take(10))
            {
                ActiveApps.Add(stats);
            }
        });
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
            var startDateOnly = DateOnly.FromDateTime(StartDate ?? DateTime.Now.AddDays(-7));
            var endDateOnly = DateOnly.FromDateTime(EndDate ?? DateTime.Now);

            var usages = await _persistence.GetAllAppUsageAsync(startDateOnly, endDateOnly);
            var usageList = usages.ToList();

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                usageList = usageList
                    .Where(u => u.AppName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                               u.ProcessName.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            AllApps.Clear();
            foreach (var usage in usageList)
            {
                AllApps.Add(usage);
            }

            // Calculate totals
            AppCount = usageList.Count;
            var totalDown = usageList.Sum(u => u.BytesReceived);
            var totalUp = usageList.Sum(u => u.BytesSent);
            TotalDownload = ByteFormatter.FormatBytes(totalDown);
            TotalUpload = ByteFormatter.FormatBytes(totalUp);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RequestElevationAsync()
    {
        IsRequestingElevation = true;
        try
        {
#if WINDOWS
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(info);
            Environment.Exit(0);
#endif
        }
        catch
        {
            // Failed to elevate
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
}

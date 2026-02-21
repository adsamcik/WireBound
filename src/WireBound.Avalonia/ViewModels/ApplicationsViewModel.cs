using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using WireBound.Core.Helpers;
using WireBound.Core.Models;
using WireBound.Core.Services;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Applications page - displays network usage per application.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Byte Tracking Limitation:</strong> Per-app byte counters require the elevated helper
/// process to provide accurate values. Without the helper:
/// </para>
/// <list type="bullet">
///   <item>Active apps with network connections are listed correctly</item>
///   <item>Historical usage records from the database display correctly</item>
///   <item>Real-time byte counters show estimated values based on proportional traffic</item>
///   <item>The <see cref="IsByteTrackingLimited"/> property indicates when estimates are in use</item>
/// </list>
/// <para>
/// This limitation exists because per-socket byte accounting requires elevated access:
/// ETW on Windows, eBPF on Linux. See docs/LIMITATIONS.md for details.
/// </para>
/// </remarks>
public sealed partial class ApplicationsViewModel : ObservableObject, IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly IDataPersistenceService _persistence;
    private readonly IProcessNetworkService? _processNetworkService;
    private readonly IElevationService _elevationService;
    private readonly ILogger<ApplicationsViewModel>? _logger;
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isPlatformSupported = true;

    [ObservableProperty]
    private bool _isPerAppTrackingEnabled;

    [ObservableProperty]
    private bool _requiresElevation;

    /// <summary>
    /// Indicates that per-app byte tracking is using estimated values
    /// because the elevated helper process is not connected.
    /// </summary>
    [ObservableProperty]
    private bool _isByteTrackingLimited;

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
        IUiDispatcher dispatcher,
        IDataPersistenceService persistence,
        IProcessNetworkService processNetworkService,
        IElevationService elevationService,
        ILogger<ApplicationsViewModel>? logger = null)
    {
        _dispatcher = dispatcher;
        _persistence = persistence;
        _processNetworkService = processNetworkService;
        _elevationService = elevationService;
        _logger = logger;

        // Per-app network tracking requires IProcessNetworkService which is now implemented
        IsPlatformSupported = _processNetworkService?.IsPlatformSupported ?? false;
        IsPerAppTrackingEnabled = _processNetworkService?.IsRunning == true;

        // Check if helper is needed but not connected
        RequiresElevation = _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring)
                            && _elevationService.IsElevationSupported
                            && !_elevationService.IsHelperConnected;

        // Byte tracking is limited when elevated helper is not connected
        IsByteTrackingLimited = !_elevationService.IsHelperConnected;

        if (_processNetworkService != null)
        {
            _processNetworkService.StatsUpdated += OnProcessStatsUpdated;
            _processNetworkService.ErrorOccurred += OnProcessErrorOccurred;
        }

        // Subscribe to helper state changes
        _elevationService.HelperConnectionStateChanged += OnHelperConnectionStateChanged;

        _ = InitializeAsync();
    }

    private void OnHelperConnectionStateChanged(object? sender, HelperConnectionStateChangedEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            RequiresElevation = !e.IsConnected
                               && _elevationService.IsElevationSupported
                               && _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring);
            IsByteTrackingLimited = !e.IsConnected;
        });
    }

    private async Task InitializeAsync()
    {
        var token = _cts.Token;

        // Load settings to check if per-app tracking is enabled
        var settings = await _persistence.GetSettingsAsync();

        if (token.IsCancellationRequested) return;

        if (settings.IsPerAppTrackingEnabled && _processNetworkService != null)
        {
            // Only start monitoring if the setting is enabled
            await StartMonitoringAsync();
        }

        if (token.IsCancellationRequested) return;

        await LoadDataAsync();
    }

    private async Task StartMonitoringAsync()
    {
        if (_processNetworkService == null) return;

        var started = await _processNetworkService.StartAsync();
        IsPerAppTrackingEnabled = started;
    }

    private void OnProcessErrorOccurred(object? sender, ProcessNetworkErrorEventArgs e)
    {
        _dispatcher.Post(() =>
        {
            // Update elevation requirement based on error, but respect platform support
            if (e.RequiresElevation && _elevationService.IsElevationSupported)
            {
                RequiresElevation = true;
            }
        });
    }

    private void OnProcessStatsUpdated(object? sender, ProcessStatsUpdatedEventArgs e)
    {
        _dispatcher.Post(() =>
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
        if (_disposed) return;

        IsLoading = true;

        try
        {
            var startDateOnly = DateOnly.FromDateTime(StartDate ?? DateTime.Now.AddDays(-7));
            var endDateOnly = DateOnly.FromDateTime(EndDate ?? DateTime.Now);

            var usages = await _persistence.GetAllAppUsageAsync(startDateOnly, endDateOnly);

            if (_cts.Token.IsCancellationRequested) return;

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
        if (!_elevationService.IsElevationSupported)
        {
            _logger?.LogWarning("Elevation requested but not supported on this platform");
            return;
        }

        if (_elevationService.IsHelperConnected)
        {
            _logger?.LogDebug("Helper already connected, no elevation needed");
            return;
        }

        IsRequestingElevation = true;
        try
        {
            _logger?.LogInformation("User requested to start elevated helper from Applications view");

            // Start the minimal helper process (NOT elevate the entire app)
            var result = await _elevationService.StartHelperAsync();

            if (result.IsSuccess)
            {
                _logger?.LogInformation("Helper process started successfully");
                RequiresElevation = false;
                // Restart monitoring now that we have elevated access
                await StartMonitoringAsync();
            }
            else if (result.Status == ElevationStatus.Cancelled)
            {
                _logger?.LogInformation("User cancelled helper elevation request");
            }
            else
            {
                _logger?.LogWarning("Failed to start helper: {Error}", result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error during helper start request");
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

    /// <summary>
    /// Disposes resources and unsubscribes from events.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();

        if (_processNetworkService != null)
        {
            _processNetworkService.StatsUpdated -= OnProcessStatsUpdated;
            _processNetworkService.ErrorOccurred -= OnProcessErrorOccurred;
        }

        _elevationService.HelperConnectionStateChanged -= OnHelperConnectionStateChanged;
    }
}

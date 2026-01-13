using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WireBound.Models;
using WireBound.Services;

namespace WireBound.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IDataPersistenceService _persistence;
    private readonly INetworkMonitorService _networkMonitor;
    private readonly INetworkPollingBackgroundService _pollingService;
    private readonly IElevationService _elevationService;
    private readonly IStartupService _startupService;
    private CancellationTokenSource _statusCts = new();
    private bool _disposed;

    [ObservableProperty]
    public partial int PollingIntervalMs { get; set; }

    [ObservableProperty]
    public partial int SaveIntervalSeconds { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool IsStartupDisabledByUser { get; set; }

    [ObservableProperty]
    public partial bool IsStartupDisabledByPolicy { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial bool UseIpHelperApi { get; set; }

    [ObservableProperty]
    public partial int DataRetentionDays { get; set; }

    [ObservableProperty]
    public partial bool IsPerAppTrackingEnabled { get; set; }

    [ObservableProperty]
    public partial int AppDataRetentionDays { get; set; }

    [ObservableProperty]
    public partial int AppDataAggregateAfterDays { get; set; }

    [ObservableProperty]
    public partial string SelectedTheme { get; set; }

    [ObservableProperty]
    public partial bool IsElevated { get; set; }

    [ObservableProperty]
    public partial bool RequiresElevation { get; set; }

    [ObservableProperty]
    public partial bool IsRequestingElevation { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<NetworkAdapter> Adapters { get; set; }

    [ObservableProperty]
    public partial NetworkAdapter? SelectedAdapter { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    public ObservableCollection<string> Themes { get; } = new() { "Light", "Dark", "System" };
    public ObservableCollection<int> PollingIntervals { get; } = new() { 500, 1000, 2000, 5000 };
    public ObservableCollection<int> SaveIntervals { get; } = new() { 30, 60, 120, 300 };

    public SettingsViewModel(
        IDataPersistenceService persistence, 
        INetworkMonitorService networkMonitor,
        INetworkPollingBackgroundService pollingService,
        IElevationService elevationService,
        IStartupService startupService)
    {
        _persistence = persistence;
        _networkMonitor = networkMonitor;
        _pollingService = pollingService;
        _elevationService = elevationService;
        _startupService = startupService;

        // Initialize observable properties
        PollingIntervalMs = 1000;
        SaveIntervalSeconds = 60;
        StartWithWindows = false;
        MinimizeToTray = true;
        UseIpHelperApi = false;
        DataRetentionDays = 365;
        IsPerAppTrackingEnabled = false;
        AppDataRetentionDays = 0;
        AppDataAggregateAfterDays = 7;
        SelectedTheme = "Dark";
        Adapters = [];
        StatusMessage = string.Empty;

        // Initialize elevation status
        IsElevated = _elevationService.IsElevated;
        RequiresElevation = _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring);

        // Initialize settings loading
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        try
        {
            await LoadSettingsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SettingsViewModel.LoadSettingsAsync failed: {ex.Message}");
        }
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _persistence.GetSettingsAsync().ConfigureAwait(false);
        
        // Load actual startup state from the system (not from saved settings)
        var startupState = await _startupService.GetStartupStateAsync().ConfigureAwait(false);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PollingIntervalMs = settings.PollingIntervalMs;
            SaveIntervalSeconds = settings.SaveIntervalSeconds;
            
            // Set startup state based on actual system registration
            StartWithWindows = startupState == StartupState.Enabled;
            IsStartupDisabledByUser = startupState == StartupState.DisabledByUser;
            IsStartupDisabledByPolicy = startupState == StartupState.DisabledByPolicy;
            
            MinimizeToTray = settings.MinimizeToTray;
            UseIpHelperApi = settings.UseIpHelperApi;
            DataRetentionDays = settings.DataRetentionDays;
            IsPerAppTrackingEnabled = settings.IsPerAppTrackingEnabled;
            AppDataRetentionDays = settings.AppDataRetentionDays;
            AppDataAggregateAfterDays = settings.AppDataAggregateAfterDays;
            SelectedTheme = settings.Theme;

            LoadAdapters(settings.SelectedAdapterId);
        });
    }

    private void LoadAdapters(string selectedId)
    {
        Adapters.Clear();
        Adapters.Add(new NetworkAdapter { Id = "", Name = "All Adapters" });

        foreach (var adapter in _networkMonitor.GetAdapters())
        {
            Adapters.Add(adapter);
        }

        SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == selectedId) ?? Adapters.First();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        // Apply startup registration first to verify it works
        var requestedStartup = StartWithWindows;
        var startupResult = await _startupService.SetStartupWithResultAsync(requestedStartup).ConfigureAwait(true);
        
        // Update UI with actual state from the result (avoids duplicate API call)
        var actualStartupEnabled = startupResult.State == StartupState.Enabled;
        StartWithWindows = actualStartupEnabled;
        IsStartupDisabledByUser = startupResult.State == StartupState.DisabledByUser;
        IsStartupDisabledByPolicy = startupResult.State == StartupState.DisabledByPolicy;

        // Save settings with the actual startup state (not the requested one)
        var settings = new AppSettings
        {
            Id = 1,
            PollingIntervalMs = PollingIntervalMs,
            SaveIntervalSeconds = SaveIntervalSeconds,
            StartWithWindows = actualStartupEnabled,
            MinimizeToTray = MinimizeToTray,
            UseIpHelperApi = UseIpHelperApi,
            DataRetentionDays = DataRetentionDays,
            IsPerAppTrackingEnabled = IsPerAppTrackingEnabled,
            AppDataRetentionDays = AppDataRetentionDays,
            AppDataAggregateAfterDays = AppDataAggregateAfterDays,
            Theme = SelectedTheme,
            SelectedAdapterId = SelectedAdapter?.Id ?? string.Empty
        };

        await _persistence.SaveSettingsAsync(settings).ConfigureAwait(true);

        // Check if startup registration failed when requested
        if (requestedStartup && !startupResult.Success)
        {
            if (IsStartupDisabledByUser)
            {
                StatusMessage = Strings.Settings_StartupDisabledByUser;
                await ClearStatusAfterDelayAsync().ConfigureAwait(true);
                return;
            }
            else if (IsStartupDisabledByPolicy)
            {
                StatusMessage = Strings.Settings_StartupDisabledByPolicy;
                await ClearStatusAfterDelayAsync().ConfigureAwait(true);
                return;
            }
            else
            {
                StatusMessage = Strings.Settings_StartupEnableFailed;
                await ClearStatusAfterDelayAsync().ConfigureAwait(true);
                return;
            }
        }

        // Apply changes to running services
        _networkMonitor.SetUseIpHelperApi(UseIpHelperApi);
        if (SelectedAdapter != null)
        {
            _networkMonitor.SetAdapter(SelectedAdapter.Id);
        }

        // Update polling intervals at runtime without requiring restart
        _pollingService.UpdatePollingInterval(PollingIntervalMs);
        _pollingService.UpdateSaveInterval(SaveIntervalSeconds);

        StatusMessage = Strings.Settings_SavedSuccessfully;

        // Clear status after delay with cancellation support
        await ClearStatusAfterDelayAsync();
    }

    private async Task ClearStatusAfterDelayAsync()
    {
        // Cancel any previous status clear task
        await _statusCts.CancelAsync().ConfigureAwait(false);
        _statusCts.Dispose();
        _statusCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(3000, _statusCts.Token).ConfigureAwait(true);
            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled, ignore
        }
    }

    [RelayCommand]
    private async Task CleanupDataAsync()
    {
        await _persistence.CleanupOldDataAsync(DataRetentionDays).ConfigureAwait(true);
        StatusMessage = $"Cleaned up data older than {DataRetentionDays} days.";

        await ClearStatusAfterDelayAsync();
    }

    [RelayCommand]
    private void TestIpHelperApi()
    {
        try
        {
            _networkMonitor.SetUseIpHelperApi(true);
            _networkMonitor.Poll();
            var stats = _networkMonitor.GetCurrentStats();
            _networkMonitor.SetUseIpHelperApi(UseIpHelperApi);

            StatusMessage = "IP Helper API test successful!";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"IP Helper API test failed: {ex}");
            StatusMessage = "IP Helper API test failed. Check your system configuration.";
        }
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
            StatusMessage = "Requesting administrator privileges...";
            
            var elevated = await _elevationService.RequestElevationAsync();
            
            if (!elevated)
            {
                StatusMessage = "Elevation cancelled or failed.";
                RequiresElevation = _elevationService.RequiresElevationFor(ElevatedFeature.PerProcessNetworkMonitoring);
                await ClearStatusAfterDelayAsync();
            }
            // If elevated is true, the app is restarting
        }
        finally
        {
            IsRequestingElevation = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _statusCts.Cancel();
        _statusCts.Dispose();
    }
}

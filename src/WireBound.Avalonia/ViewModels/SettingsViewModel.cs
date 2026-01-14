using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Settings page
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IDataPersistenceService _persistence;
    private readonly INetworkMonitorService _networkMonitor;
    private CancellationTokenSource? _autoSaveCts;
    private bool _isLoading = true;
    private const int AutoSaveDelayMs = 500;

    [ObservableProperty]
    private ObservableCollection<NetworkAdapter> _adapters = [];

    [ObservableProperty]
    private NetworkAdapter? _selectedAdapter;

    [ObservableProperty]
    private int _pollingIntervalMs = 1000;

    [ObservableProperty]
    private bool _useIpHelperApi;

    [ObservableProperty]
    private bool _isPerAppTrackingEnabled;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTray = true;
    
    [ObservableProperty]
    private SpeedUnit _selectedSpeedUnit = SpeedUnit.BytesPerSecond;
    
    public SpeedUnit[] SpeedUnits { get; } = Enum.GetValues<SpeedUnit>();

    [ObservableProperty]
    private bool _isElevated;

    [ObservableProperty]
    private bool _requiresElevation;

    [ObservableProperty]
    private bool _isRequestingElevation;

    [ObservableProperty]
    private bool _isStartupDisabledByUser;

    [ObservableProperty]
    private bool _isStartupDisabledByPolicy;

    public List<int> PollingIntervals { get; } = [250, 500, 1000, 2000, 5000];

    partial void OnSelectedAdapterChanged(NetworkAdapter? value) => ScheduleAutoSave();
    partial void OnPollingIntervalMsChanged(int value) => ScheduleAutoSave();
    partial void OnUseIpHelperApiChanged(bool value) => ScheduleAutoSave();
    partial void OnIsPerAppTrackingEnabledChanged(bool value) => ScheduleAutoSave();
    partial void OnStartWithWindowsChanged(bool value) => ScheduleAutoSave();
    partial void OnMinimizeToTrayChanged(bool value) => ScheduleAutoSave();
    partial void OnSelectedSpeedUnitChanged(SpeedUnit value) => ScheduleAutoSave();

    private void ScheduleAutoSave()
    {
        if (_isLoading) return;

        // Cancel any pending auto-save
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(AutoSaveDelayMs, token);
                if (!token.IsCancellationRequested)
                {
                    await SaveAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation
            }
        }, token);
    }

    public SettingsViewModel(
        IDataPersistenceService persistence,
        INetworkMonitorService networkMonitor)
    {
        _persistence = persistence;
        _networkMonitor = networkMonitor;

        LoadSettings();
    }

    private async void LoadSettings()
    {
        // Load adapters
        Adapters.Clear();
        foreach (var adapter in _networkMonitor.GetAdapters())
        {
            Adapters.Add(adapter);
        }

        // Load settings from database
        var settings = await _persistence.GetSettingsAsync();
        
        PollingIntervalMs = settings.PollingIntervalMs;
        UseIpHelperApi = settings.UseIpHelperApi;
        IsPerAppTrackingEnabled = settings.IsPerAppTrackingEnabled;
        MinimizeToTray = settings.MinimizeToTray;
        StartWithWindows = settings.StartWithWindows;
        SelectedSpeedUnit = settings.SpeedUnit;
        
        // Apply speed unit setting globally
        WireBound.Core.Helpers.ByteFormatter.UseSpeedInBits = settings.SpeedUnit == SpeedUnit.BitsPerSecond;
        
        // Find matching adapter
        SelectedAdapter = Adapters.FirstOrDefault(a => a.Id == settings.SelectedAdapterId);

        // Check elevation status
        // TODO: Add cross-platform elevation helper
        IsElevated = false;
        RequiresElevation = false;

        _isLoading = false;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var settings = new AppSettings
        {
            SelectedAdapterId = SelectedAdapter?.Id ?? "",
            PollingIntervalMs = PollingIntervalMs,
            UseIpHelperApi = UseIpHelperApi,
            IsPerAppTrackingEnabled = IsPerAppTrackingEnabled,
            MinimizeToTray = MinimizeToTray,
            StartWithWindows = StartWithWindows,
            SpeedUnit = SelectedSpeedUnit
        };
        
        // Apply speed unit setting globally
        WireBound.Core.Helpers.ByteFormatter.UseSpeedInBits = SelectedSpeedUnit == SpeedUnit.BitsPerSecond;

        await _persistence.SaveSettingsAsync(settings);

        // Apply settings
        _networkMonitor.SetUseIpHelperApi(UseIpHelperApi);
    }

    [RelayCommand]
    private async Task RequestElevationAsync()
    {
        IsRequestingElevation = true;
        try
        {
#if WINDOWS
            // Restart with elevation
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(info);
            Environment.Exit(0);
#endif
            await Task.CompletedTask;
        }
        catch
        {
            // Elevation failed - user may have cancelled UAC prompt
        }
        finally
        {
            IsRequestingElevation = false;
        }
    }
}

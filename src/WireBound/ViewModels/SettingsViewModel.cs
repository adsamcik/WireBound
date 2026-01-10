using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using WireBound.Models;
using WireBound.Services;

namespace WireBound.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IDataPersistenceService _persistence;
    private readonly INetworkMonitorService _networkMonitor;

    [ObservableProperty]
    private int _pollingIntervalMs = 1000;

    [ObservableProperty]
    private int _saveIntervalSeconds = 60;

    [ObservableProperty]
    private bool _startWithWindows = false;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _useIpHelperApi = false;

    [ObservableProperty]
    private int _dataRetentionDays = 365;

    [ObservableProperty]
    private string _selectedTheme = "Dark";

    [ObservableProperty]
    private bool _isPerAppTrackingEnabled = false;

    [ObservableProperty]
    private int _appDataRetentionDays = 90;

    [ObservableProperty]
    private int _appDataAggregateAfterDays = 7;

    [ObservableProperty]
    private ObservableCollection<NetworkAdapter> _adapters = new();

    [ObservableProperty]
    private NetworkAdapter? _selectedAdapter;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public ObservableCollection<string> Themes { get; } = new() { "Light", "Dark", "System" };
    public ObservableCollection<int> PollingIntervals { get; } = new() { 500, 1000, 2000, 5000 };
    public ObservableCollection<int> SaveIntervals { get; } = new() { 30, 60, 120, 300 };

    public SettingsViewModel(IDataPersistenceService persistence, INetworkMonitorService networkMonitor)
    {
        _persistence = persistence;
        _networkMonitor = networkMonitor;
        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _persistence.GetSettingsAsync();

        PollingIntervalMs = settings.PollingIntervalMs;
        SaveIntervalSeconds = settings.SaveIntervalSeconds;
        StartWithWindows = settings.StartWithWindows;
        MinimizeToTray = settings.MinimizeToTray;
        UseIpHelperApi = settings.UseIpHelperApi;
        DataRetentionDays = settings.DataRetentionDays;
        SelectedTheme = settings.Theme;
        IsPerAppTrackingEnabled = settings.IsPerAppTrackingEnabled;
        AppDataRetentionDays = settings.AppDataRetentionDays;
        AppDataAggregateAfterDays = settings.AppDataAggregateAfterDays;

        LoadAdapters(settings.SelectedAdapterId);
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
        var settings = new AppSettings
        {
            Id = 1,
            PollingIntervalMs = PollingIntervalMs,
            SaveIntervalSeconds = SaveIntervalSeconds,
            StartWithWindows = StartWithWindows,
            MinimizeToTray = MinimizeToTray,
            UseIpHelperApi = UseIpHelperApi,
            DataRetentionDays = DataRetentionDays,
            Theme = SelectedTheme,
            SelectedAdapterId = SelectedAdapter?.Id ?? string.Empty,
            IsPerAppTrackingEnabled = IsPerAppTrackingEnabled,
            AppDataRetentionDays = AppDataRetentionDays,
            AppDataAggregateAfterDays = AppDataAggregateAfterDays
        };

        await _persistence.SaveSettingsAsync(settings);

        // Apply changes
        _networkMonitor.SetUseIpHelperApi(UseIpHelperApi);
        if (SelectedAdapter != null)
        {
            _networkMonitor.SetAdapter(SelectedAdapter.Id);
        }

        StatusMessage = "Settings saved successfully!";

        // Clear status after delay
        await Task.Delay(3000);
        StatusMessage = string.Empty;
    }

    [RelayCommand]
    private async Task CleanupDataAsync()
    {
        await _persistence.CleanupOldDataAsync(DataRetentionDays);
        StatusMessage = $"Cleaned up data older than {DataRetentionDays} days.";

        await Task.Delay(3000);
        StatusMessage = string.Empty;
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
            StatusMessage = $"IP Helper API test failed: {ex.Message}";
        }
    }
}

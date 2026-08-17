using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using WireBound.Core;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Composes the existing network, system, and process monitors into the unified
/// resource dashboard. The underlying collectors remain the single owners of
/// their data; this view model only coordinates focus and presentation state.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private const string AllProcesses = "All processes";
    private const string UserProcesses = "User processes";

    private readonly INavigationService _navigationService;
    private bool _disposed;

    public DashboardViewModel(
        OverviewViewModel network,
        SystemViewModel system,
        AppsViewModel processes,
        INavigationService navigationService)
    {
        Network = network;
        System = system;
        Processes = processes;
        _navigationService = navigationService;

        SelectedTimeRangeOption = TimeRangeOptions[0];
        SelectedProcessScope = ProcessScopeOptions[0];
        UpdateSignalOptions();
        ApplyProcessSort();

        System.PropertyChanged += OnSystemPropertyChanged;
        Network.PropertyChanged += OnNetworkPropertyChanged;
    }

    public OverviewViewModel Network { get; }
    public SystemViewModel System { get; }
    public AppsViewModel Processes { get; }

    public static IReadOnlyList<TimeRangeDisplayItem> TimeRangeOptions => OverviewViewModel.TimeRangeOptions;

    public static IReadOnlyList<string> ProcessScopeOptions { get; } =
        [AllProcesses, UserProcesses];

    public ObservableCollection<string> SignalOptions { get; } = [];

    [ObservableProperty]
    private DashboardResource _selectedResource = DashboardResource.Network;

    [ObservableProperty]
    private bool _isAutomaticFocus = true;

    [ObservableProperty]
    private TimeRangeDisplayItem _selectedTimeRangeOption;

    [ObservableProperty]
    private string _selectedSignal = "Download + upload";

    [ObservableProperty]
    private string _selectedProcessScope;

    [ObservableProperty]
    private bool _isFilterPanelVisible;

    public bool IsNetworkFocus => SelectedResource == DashboardResource.Network;
    public bool IsCpuFocus => SelectedResource == DashboardResource.Cpu;
    public bool IsMemoryFocus => SelectedResource == DashboardResource.Memory;
    public bool IsDiskFocus => SelectedResource == DashboardResource.Disk;

    public string FocusTitle => SelectedResource switch
    {
        DashboardResource.Cpu => "CPU activity",
        DashboardResource.Memory => "Memory activity",
        DashboardResource.Disk => "Disk activity",
        _ => "Network activity"
    };

    public string FocusSubtitle => SelectedResource switch
    {
        DashboardResource.Cpu => "Total processor utilization over the selected range",
        DashboardResource.Memory => "Physical memory utilization over the selected range",
        DashboardResource.Disk => "Read and write throughput over the selected range",
        _ => "Download and upload throughput over the selected range"
    };

    public string ContributorsTitle => SelectedResource switch
    {
        DashboardResource.Cpu => "CPU consumers",
        DashboardResource.Memory => "Memory consumers",
        DashboardResource.Disk => "Disk attribution",
        _ => "Network consumers"
    };

    public string CurrentMetric => SelectedResource switch
    {
        DashboardResource.Cpu => System.CpuUsageFormatted,
        DashboardResource.Memory => $"{System.MemoryUsed} / {System.MemoryTotal}",
        DashboardResource.Disk => $"↓ {System.DiskRead}  ↑ {System.DiskWrite}",
        _ => $"↓ {Network.DownloadSpeed}  ↑ {Network.UploadSpeed}"
    };

    public IEnumerable<ISeries> NetworkSeries => SelectedSignal switch
    {
        "Download" => Network.ChartSeries.Where(series => series.Name == "Download"),
        "Upload" => Network.ChartSeries.Where(series => series.Name == "Upload"),
        _ => Network.ChartSeries
    };

    public IEnumerable<ISeries> DiskSeries => SelectedSignal switch
    {
        "Read" => System.DiskSeries.Where(series => series.Name == "Read"),
        "Write" => System.DiskSeries.Where(series => series.Name == "Write"),
        _ => System.DiskSeries
    };

    [RelayCommand]
    private void SelectResource(DashboardResource resource)
    {
        IsAutomaticFocus = false;
        SelectedResource = resource;
    }

    [RelayCommand]
    private void EnableAutomaticFocus()
    {
        IsAutomaticFocus = true;
        SelectAutomaticResource();
    }

    [RelayCommand]
    private void ToggleFilters() => IsFilterPanelVisible = !IsFilterPanelVisible;

    [RelayCommand]
    private void OpenResourceDetails()
    {
        _navigationService.NavigateTo(IsNetworkFocus ? Routes.Charts : Routes.System);
    }

    [RelayCommand]
    private void OpenProcesses() => _navigationService.NavigateTo(Routes.Apps);

    [RelayCommand]
    private void OpenTimeline() => _navigationService.NavigateTo(Routes.History);

    partial void OnSelectedResourceChanged(DashboardResource value)
    {
        UpdateSignalOptions();
        ApplyProcessSort();
        NotifyFocusProperties();
    }

    partial void OnSelectedTimeRangeOptionChanged(TimeRangeDisplayItem value)
    {
        Network.SelectedTimeRange = value.Value;
        System.SetLiveWindow(TimeSpan.FromSeconds(value.Seconds));
    }

    partial void OnSelectedSignalChanged(string value)
    {
        OnPropertyChanged(nameof(NetworkSeries));
        OnPropertyChanged(nameof(DiskSeries));
        ApplyProcessSort();
    }

    partial void OnSelectedProcessScopeChanged(string value)
    {
        Processes.ShowSystemProcesses = value == AllProcesses;
    }

    private void OnSystemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemViewModel.CpuUsageFormatted)
            or nameof(SystemViewModel.MemoryUsed)
            or nameof(SystemViewModel.MemoryTotal)
            or nameof(SystemViewModel.DiskRead)
            or nameof(SystemViewModel.DiskWrite))
        {
            OnPropertyChanged(nameof(CurrentMetric));
        }

        if (IsAutomaticFocus && e.PropertyName is nameof(SystemViewModel.CpuUsagePercent)
            or nameof(SystemViewModel.MemoryUsagePercent)
            or nameof(SystemViewModel.DiskActivityPercent))
        {
            SelectAutomaticResource();
        }
    }

    private void OnNetworkPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OverviewViewModel.DownloadSpeed)
            or nameof(OverviewViewModel.UploadSpeed))
        {
            OnPropertyChanged(nameof(CurrentMetric));
        }
    }

    private void SelectAutomaticResource()
    {
        var next = System.MemoryUsagePercent >= 85
            ? DashboardResource.Memory
            : System.CpuUsagePercent >= 85
                ? DashboardResource.Cpu
                : System.DiskActivityPercent >= 85
                    ? DashboardResource.Disk
                    : DashboardResource.Network;

        SelectedResource = next;
    }

    private void UpdateSignalOptions()
    {
        var options = SelectedResource switch
        {
            DashboardResource.Network => new[] { "Download + upload", "Download", "Upload" },
            DashboardResource.Disk => new[] { "Read + write", "Read", "Write" },
            DashboardResource.Cpu => new[] { "Total CPU" },
            _ => new[] { "Used memory" }
        };

        SignalOptions.Clear();
        foreach (var option in options)
        {
            SignalOptions.Add(option);
        }

        SelectedSignal = options[0];
    }

    private void ApplyProcessSort()
    {
        var sort = SelectedResource switch
        {
            DashboardResource.Cpu => ProcessUsageSortColumn.Cpu,
            DashboardResource.Memory => ProcessUsageSortColumn.Memory,
            DashboardResource.Network when SelectedSignal == "Upload" => ProcessUsageSortColumn.Upload,
            _ => ProcessUsageSortColumn.Download
        };

        Processes.SetSort(sort, descending: true);
    }

    private void NotifyFocusProperties()
    {
        OnPropertyChanged(nameof(IsNetworkFocus));
        OnPropertyChanged(nameof(IsCpuFocus));
        OnPropertyChanged(nameof(IsMemoryFocus));
        OnPropertyChanged(nameof(IsDiskFocus));
        OnPropertyChanged(nameof(FocusTitle));
        OnPropertyChanged(nameof(FocusSubtitle));
        OnPropertyChanged(nameof(ContributorsTitle));
        OnPropertyChanged(nameof(CurrentMetric));
        OnPropertyChanged(nameof(NetworkSeries));
        OnPropertyChanged(nameof(DiskSeries));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        System.PropertyChanged -= OnSystemPropertyChanged;
        Network.PropertyChanged -= OnNetworkPropertyChanged;
    }
}

public enum DashboardResource
{
    Network,
    Cpu,
    Memory,
    Disk
}

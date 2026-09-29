using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    private const int DashboardContributorLimit = 5;
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

        Processes.PropertyChanged += OnProcessesPropertyChanged;
        Processes.ProcessItems.CollectionChanged += OnProcessItemsChanged;
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
    public bool HasSignalChoices => IsNetworkFocus || IsDiskFocus;
    public bool HasContextFilters => IsNetworkFocus;
    public bool HasProcessAttribution => !IsDiskFocus;

    /// <summary>
    /// Keeps the dashboard scannable. The full, searchable process inventory is
    /// available from the Processes drill-down.
    /// </summary>
    public IReadOnlyList<ProcessUsageDisplayItem> TopContributors =>
        Processes.ProcessItems.Take(DashboardContributorLimit).ToArray();

    public string ContributorsSubtitle
    {
        get
        {
            if (Processes.IsLoading)
            {
                return "Collecting the first process sample…";
            }

            var count = Processes.VisibleProcessCount;
            return count switch
            {
                0 => "No matching processes in the current scope",
                <= DashboardContributorLimit => $"{count} active {(count == 1 ? "process" : "processes")}, ranked by this resource",
                _ => $"Top {DashboardContributorLimit} of {count} processes right now"
            };
        }
    }

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
        SelectedResource = resource;
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
        if (!IsNetworkFocus)
        {
            IsFilterPanelVisible = false;
        }

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

    private void OnProcessesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppsViewModel.VisibleProcessCount)
            or nameof(AppsViewModel.IsLoading))
        {
            OnPropertyChanged(nameof(ContributorsSubtitle));
        }
    }

    private void OnProcessItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(TopContributors));

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
        OnPropertyChanged(nameof(HasSignalChoices));
        OnPropertyChanged(nameof(HasContextFilters));
        OnPropertyChanged(nameof(HasProcessAttribution));
        OnPropertyChanged(nameof(FocusTitle));
        OnPropertyChanged(nameof(FocusSubtitle));
        OnPropertyChanged(nameof(ContributorsTitle));
        OnPropertyChanged(nameof(NetworkSeries));
        OnPropertyChanged(nameof(DiskSeries));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Processes.PropertyChanged -= OnProcessesPropertyChanged;
        Processes.ProcessItems.CollectionChanged -= OnProcessItemsChanged;
    }
}

public enum DashboardResource
{
    Network,
    Cpu,
    Memory,
    Disk
}

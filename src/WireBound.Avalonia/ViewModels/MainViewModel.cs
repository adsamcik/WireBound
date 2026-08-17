using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WireBound.Avalonia.Messages;
using WireBound.Avalonia.Services;
using WireBound.Core;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Main view model handling navigation and app state
/// </summary>
public partial class MainViewModel : ObservableObject, IRecipient<UpdateAvailableMessage>, IRecipient<MemoryPressureMessage>, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly IViewFactory _viewFactory;
    private readonly INetworkMonitorService _networkMonitor;
    private readonly ITrayIconService _trayIconService;
    private bool _disposed;
    private bool _isSynchronizingNavigation;

    /// <summary>
    /// Gets the application version from the assembly
    /// </summary>
    public string Version { get; } = GetAppVersion();
    public string MonitoringStatusText => IsMonitoringActive ? "Monitoring Active" : "Monitoring Inactive";
    public string MonitoringStatusAutomationName => $"Monitoring Status: {(IsMonitoringActive ? "Active" : "Inactive")}";

    private static string GetAppVersion()
    {
        var version = typeof(MainViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "Unknown";

        // Remove any metadata after '+' (e.g., commit hash)
        var plusIndex = version.IndexOf('+');
        if (plusIndex > 0)
            version = version[..plusIndex];

        return $"v{version}";
    }

    public MainViewModel(
        INavigationService navigationService,
        IViewFactory viewFactory,
        INetworkMonitorService networkMonitor,
        ITrayIconService trayIconService,
        DashboardViewModel? dashboard = null)
    {
        _navigationService = navigationService;
        _viewFactory = viewFactory;
        _networkMonitor = networkMonitor;
        _trayIconService = trayIconService;
        Dashboard = dashboard;

        // Initialize navigation items
        NavigationItems =
        [
            new NavigationItem { Title = "Overview",    IconKey = "WbNavOverview",    Route = Routes.Overview },
            new NavigationItem { Title = "Processes",   IconKey = "WbNavApps",        Route = Routes.Apps },
            new NavigationItem { Title = "Connections", IconKey = "WbNavConnections", Route = Routes.Connections }
        ];

        _selectedNavigationItem = NavigationItems[0];
        _currentView = _viewFactory.CreateView(Routes.Overview);

        _navigationService.NavigationChanged += OnNavigationChanged;
        _networkMonitor.StatsUpdated += OnNetworkStatsUpdated;
        IsMonitoringActive = false;

        // Register for update badge messages and memory pressure messages
        WeakReferenceMessenger.Default.RegisterAll(this);
    }

    /// <summary>
    /// Receives update available messages and sets the top-bar Settings badge.
    /// </summary>
    public void Receive(UpdateAvailableMessage message)
    {
        SettingsHasBadge = true;
    }

    /// <summary>
    /// Receives memory pressure messages and forwards them to the tray icon service.
    /// </summary>
    public void Receive(MemoryPressureMessage message)
    {
        _trayIconService.UpdateMemoryPressure(message.Level, message.UsagePercent, message.AvailableBytes, message.SwapUsedBytes);
    }

    public List<NavigationItem> NavigationItems { get; }

    public DashboardViewModel? Dashboard { get; }

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private object? _currentView;

    [ObservableProperty]
    private bool _isMonitoringActive;

    [ObservableProperty]
    private bool _settingsHasBadge;

    [ObservableProperty]
    private string _currentRoute = Routes.Overview;

    public bool IsOverviewSelected => CurrentRoute is Routes.Overview or Routes.Charts or Routes.System or Routes.History;
    public bool IsDashboardRoute => CurrentRoute == Routes.Overview;
    public bool IsProcessesSelected => CurrentRoute == Routes.Apps;
    public bool IsConnectionsSelected => CurrentRoute == Routes.Connections;
    public bool IsSettingsSelected => CurrentRoute == Routes.Settings;

    partial void OnIsMonitoringActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(MonitoringStatusText));
        OnPropertyChanged(nameof(MonitoringStatusAutomationName));
    }

    partial void OnSelectedNavigationItemChanged(NavigationItem? value)
    {
        if (value != null && !_isSynchronizingNavigation)
        {
            _navigationService.NavigateTo(value.Route);
        }
    }

    private void OnNavigationChanged(string route)
    {
        CurrentView = _viewFactory.CreateView(route);
        CurrentRoute = route;

        _isSynchronizingNavigation = true;
        SelectedNavigationItem = route switch
        {
            Routes.Apps => NavigationItems.First(item => item.Route == Routes.Apps),
            Routes.Connections => NavigationItems.First(item => item.Route == Routes.Connections),
            Routes.Settings => null,
            _ => NavigationItems.First(item => item.Route == Routes.Overview)
        };
        _isSynchronizingNavigation = false;

        if (route == Routes.Settings)
        {
            SettingsHasBadge = false;
        }

        OnPropertyChanged(nameof(IsOverviewSelected));
        OnPropertyChanged(nameof(IsDashboardRoute));
        OnPropertyChanged(nameof(IsProcessesSelected));
        OnPropertyChanged(nameof(IsConnectionsSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    private void OnNetworkStatsUpdated(object? sender, NetworkStats _)
    {
        if (IsMonitoringActive)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            IsMonitoringActive = true;
            return;
        }

        Dispatcher.UIThread.Post(() => IsMonitoringActive = true);
    }

    [RelayCommand]
    private void NavigateTo(string route)
    {
        _navigationService.NavigateTo(route);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _navigationService.NavigationChanged -= OnNavigationChanged;
        _networkMonitor.StatsUpdated -= OnNetworkStatsUpdated;
        WeakReferenceMessenger.Default.Unregister<UpdateAvailableMessage>(this);
        WeakReferenceMessenger.Default.Unregister<MemoryPressureMessage>(this);
    }
}

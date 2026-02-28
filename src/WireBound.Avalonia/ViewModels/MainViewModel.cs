using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WireBound.Avalonia.Services;
using WireBound.Core;
using WireBound.Core.Models;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Main view model handling navigation and app state
/// </summary>
public partial class MainViewModel : ObservableObject, IRecipient<UpdateAvailableMessage>, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly IViewFactory _viewFactory;
    private readonly INetworkMonitorService _networkMonitor;
    private bool _disposed;

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
        INetworkMonitorService networkMonitor)
    {
        _navigationService = navigationService;
        _viewFactory = viewFactory;
        _networkMonitor = networkMonitor;

        // Initialize navigation items
        NavigationItems =
        [
            new NavigationItem { Title = "Overview", Icon = "📊", Route = Routes.Overview },
            new NavigationItem { Title = "Live Chart", Icon = "📈", Route = Routes.Charts },
            new NavigationItem { Title = "System", Icon = "💻", Route = Routes.System },
            new NavigationItem { Title = "Applications", Icon = "📱", Route = Routes.Applications },
            new NavigationItem { Title = "Connections", Icon = "🔗", Route = Routes.Connections },
            new NavigationItem { Title = "Insights", Icon = "💡", Route = Routes.Insights },
            new NavigationItem { Title = "Settings", Icon = "⚙️", Route = Routes.Settings }
        ];

        _selectedNavigationItem = NavigationItems[0];
        _currentView = _viewFactory.CreateView(Routes.Overview);

        _navigationService.NavigationChanged += OnNavigationChanged;
        _networkMonitor.StatsUpdated += OnNetworkStatsUpdated;
        IsMonitoringActive = false;

        // Register for update badge messages
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// Receives update available messages and sets the badge on the Settings nav item.
    /// </summary>
    public void Receive(UpdateAvailableMessage message)
    {
        var settingsItem = NavigationItems.FirstOrDefault(n => n.Route == Routes.Settings);
        if (settingsItem != null)
        {
            settingsItem.HasBadge = true;
        }
    }

    public List<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItem _selectedNavigationItem;

    [ObservableProperty]
    private object? _currentView;
    
    [ObservableProperty]
    private bool _isMonitoringActive;

    partial void OnIsMonitoringActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(MonitoringStatusText));
        OnPropertyChanged(nameof(MonitoringStatusAutomationName));
    }

    partial void OnSelectedNavigationItemChanged(NavigationItem value)
    {
        if (value != null)
        {
            _navigationService.NavigateTo(value.Route);

            // Clear badge when navigating to Settings
            if (value.Route == Routes.Settings)
            {
                value.HasBadge = false;
            }
        }
    }

    private void OnNavigationChanged(string route)
    {
        CurrentView = _viewFactory.CreateView(route);
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
    }
}

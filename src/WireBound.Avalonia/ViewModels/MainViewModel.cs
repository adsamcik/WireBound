using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WireBound.Avalonia.Services;
using WireBound.Avalonia.Views;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Navigation item for the sidebar
/// </summary>
public partial class NavigationItem : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private string _route = string.Empty;
}

/// <summary>
/// Main view model handling navigation and app state
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    public MainViewModel(
        INavigationService navigationService,
        DashboardViewModel dashboardViewModel,
        ChartsViewModel chartsViewModel,
        HistoryViewModel historyViewModel,
        SettingsViewModel settingsViewModel,
        ApplicationsViewModel applicationsViewModel)
    {
        _navigationService = navigationService;
        
        // Store view models for navigation
        _viewModels = new Dictionary<string, object>
        {
            { "Dashboard", dashboardViewModel },
            { "Charts", chartsViewModel },
            { "History", historyViewModel },
            { "Settings", settingsViewModel },
            { "Applications", applicationsViewModel }
        };

        // Initialize navigation items
        NavigationItems =
        [
            new NavigationItem { Title = "Dashboard", Icon = "📊", Route = "Dashboard" },
            new NavigationItem { Title = "Live Chart", Icon = "📈", Route = "Charts" },
            new NavigationItem { Title = "Applications", Icon = "📱", Route = "Applications" },
            new NavigationItem { Title = "History", Icon = "📜", Route = "History" },
            new NavigationItem { Title = "Settings", Icon = "⚙️", Route = "Settings" }
        ];

        _selectedNavigationItem = NavigationItems[0];
        _currentView = CreateViewForRoute("Dashboard");

        _navigationService.NavigationChanged += OnNavigationChanged;
    }

    private readonly Dictionary<string, object> _viewModels;

    public List<NavigationItem> NavigationItems { get; }

    [ObservableProperty]
    private NavigationItem _selectedNavigationItem;

    [ObservableProperty]
    private object? _currentView;

    partial void OnSelectedNavigationItemChanged(NavigationItem value)
    {
        if (value != null)
        {
            _navigationService.NavigateTo(value.Route);
        }
    }

    private void OnNavigationChanged(string route)
    {
        CurrentView = CreateViewForRoute(route);
    }

    private object CreateViewForRoute(string route)
    {
        return route switch
        {
            "Dashboard" => new DashboardView { DataContext = _viewModels["Dashboard"] },
            "Charts" => new ChartsView { DataContext = _viewModels["Charts"] },
            "History" => new HistoryView { DataContext = _viewModels["History"] },
            "Settings" => new SettingsView { DataContext = _viewModels["Settings"] },
            "Applications" => new ApplicationsView { DataContext = _viewModels["Applications"] },
            _ => new DashboardView { DataContext = _viewModels["Dashboard"] }
        };
    }

    [RelayCommand]
    private void NavigateTo(string route)
    {
        _navigationService.NavigateTo(route);
    }
}

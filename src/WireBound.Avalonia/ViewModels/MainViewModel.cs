using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WireBound.Avalonia.Services;
using WireBound.Core.Services;

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
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly INavigationService _navigationService;
    private readonly IViewFactory _viewFactory;
    private bool _disposed;

    /// <summary>
    /// Gets the application version from the assembly
    /// </summary>
    public string Version { get; } = GetAppVersion();

    private static string GetAppVersion()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "Unknown";
        
        // Remove any metadata after '+' (e.g., commit hash)
        var plusIndex = version.IndexOf('+');
        if (plusIndex > 0)
            version = version[..plusIndex];
        
        return $"v{version}";
    }

    public MainViewModel(
        INavigationService navigationService,
        IViewFactory viewFactory)
    {
        _navigationService = navigationService;
        _viewFactory = viewFactory;

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
        _currentView = _viewFactory.CreateView("Dashboard");

        _navigationService.NavigationChanged += OnNavigationChanged;
    }

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
        CurrentView = _viewFactory.CreateView(route);
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
    }
}

using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WireBound.Avalonia.Services;
using WireBound.Core;
using WireBound.Core.Services;

namespace WireBound.Avalonia.ViewModels;

/// <summary>
/// Message sent when an update is available, so MainViewModel can show a badge.
/// </summary>
public record UpdateAvailableMessage(string Version);

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

    [ObservableProperty]
    private bool _hasBadge;
}

/// <summary>
/// Main view model handling navigation and app state
/// </summary>
public partial class MainViewModel : ObservableObject, IRecipient<UpdateAvailableMessage>, IDisposable
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
        WeakReferenceMessenger.Default.Unregister<UpdateAvailableMessage>(this);
    }
}

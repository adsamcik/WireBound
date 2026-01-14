namespace WireBound.Avalonia.Services;

/// <summary>
/// Navigation service interface for switching between views
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Gets or sets the current view name
    /// </summary>
    string CurrentView { get; }

    /// <summary>
    /// Event raised when navigation occurs
    /// </summary>
    event Action<string>? NavigationChanged;

    /// <summary>
    /// Navigate to a named view
    /// </summary>
    void NavigateTo(string viewName);
}

/// <summary>
/// Navigation service implementation
/// </summary>
public sealed class NavigationService : INavigationService
{
    public string CurrentView { get; private set; } = "Dashboard";

    public event Action<string>? NavigationChanged;

    public void NavigateTo(string viewName)
    {
        if (CurrentView != viewName)
        {
            CurrentView = viewName;
            NavigationChanged?.Invoke(viewName);
        }
    }
}

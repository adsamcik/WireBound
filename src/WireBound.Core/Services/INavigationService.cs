namespace WireBound.Core.Services;

/// <summary>
/// Navigation service interface for switching between views.
/// Defined in Core to enable testing and reuse across UI platforms.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Gets the current view name.
    /// </summary>
    string CurrentView { get; }

    /// <summary>
    /// Event raised when navigation occurs.
    /// </summary>
    event Action<string>? NavigationChanged;

    /// <summary>
    /// Navigate to a named view.
    /// </summary>
    /// <param name="viewName">The name of the view to navigate to.</param>
    void NavigateTo(string viewName);
}

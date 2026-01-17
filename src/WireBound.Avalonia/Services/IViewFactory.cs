using Avalonia.Controls;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Factory for creating Views with their ViewModels via DI.
/// </summary>
public interface IViewFactory
{
    /// <summary>
    /// Creates a View for the specified route with its associated ViewModel.
    /// </summary>
    /// <param name="route">The route name (e.g., "Dashboard", "Charts").</param>
    /// <returns>The View control with DataContext set to the appropriate ViewModel.</returns>
    Control CreateView(string route);
}

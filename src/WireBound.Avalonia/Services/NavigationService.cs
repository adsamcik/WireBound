using WireBound.Core;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Navigation service implementation for Avalonia UI.
/// </summary>
public sealed class NavigationService : INavigationService
{
    public string CurrentView { get; private set; } = Routes.Overview;

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

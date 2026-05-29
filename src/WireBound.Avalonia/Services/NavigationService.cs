using WireBound.Core;
using WireBound.Core.Services;

namespace WireBound.Avalonia.Services;

/// <summary>
/// Navigation service implementation for Avalonia UI.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private const string LegacyApplicationsRoute = "Applications";
    private const string LegacyInsightsRoute = "Insights";

    public string CurrentView { get; private set; } = Routes.Overview;

    public event Action<string>? NavigationChanged;

    public void NavigateTo(string viewName)
    {
        var route = NormalizeRoute(viewName);

        if (CurrentView != route)
        {
            CurrentView = route;
            NavigationChanged?.Invoke(route);
        }
    }

    private static string NormalizeRoute(string route)
    {
        return route switch
        {
            LegacyApplicationsRoute or LegacyInsightsRoute => Routes.Apps,
            _ => route
        };
    }
}

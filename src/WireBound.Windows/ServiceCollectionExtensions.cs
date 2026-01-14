using Microsoft.Extensions.DependencyInjection;
using WireBound.Core.Services;
using WireBound.Windows.Services;

namespace WireBound.Windows;

/// <summary>
/// Extension methods for registering Windows-specific platform services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Windows-specific platform service implementations to the service collection.
    /// </summary>
    public static IServiceCollection AddWindowsPlatformServices(this IServiceCollection services)
    {
        // Core service implementations
        services.AddSingleton<INetworkMonitorService, WindowsNetworkMonitorService>();
        services.AddSingleton<ITrayIconService, WindowsTrayIconService>();
        services.AddSingleton<IStartupService, WindowsStartupService>();
        services.AddSingleton<IElevationService, WindowsElevationService>();
        
        // Windows-specific helper services
        services.AddSingleton<IWindowsRegistryService, WindowsRegistryService>();
        services.AddSingleton<IWindowsStartupTaskService, WindowsStartupTaskService>();
        
        return services;
    }
}

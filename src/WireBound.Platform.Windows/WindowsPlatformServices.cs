using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WireBound.Platform.Abstract;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Windows.Services;

namespace WireBound.Platform.Windows;

/// <summary>
/// Registers Windows-specific platform services.
/// Uses Replace to override stub registrations instead of adding duplicates.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformServices : IPlatformServices
{
    public static readonly WindowsPlatformServices Instance = new();

    private WindowsPlatformServices() { }

    public void Register(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IStartupService, WindowsStartupService>());
        services.Replace(ServiceDescriptor.Singleton<IWiFiInfoProvider, WindowsWiFiInfoProvider>());
        services.Replace(ServiceDescriptor.Singleton<IProcessNetworkProviderFactory, WindowsProcessNetworkProviderFactory>());
        services.Replace(ServiceDescriptor.Singleton<IElevationService, WindowsElevationService>());
        services.Replace(ServiceDescriptor.Singleton<IHelperProcessManager, WindowsHelperProcessManager>());
        services.Replace(ServiceDescriptor.Singleton<ICpuInfoProvider, WindowsCpuInfoProvider>());
        services.Replace(ServiceDescriptor.Singleton<IMemoryInfoProvider, WindowsMemoryInfoProvider>());
        services.Replace(ServiceDescriptor.Singleton<INetworkCostProvider, WindowsNetworkCostProvider>());
        services.Replace(ServiceDescriptor.Singleton<IProcessResourceProvider, WindowsProcessResourceProvider>());
    }
}

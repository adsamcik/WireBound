using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WireBound.Platform.Abstract;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Linux.Services;

namespace WireBound.Platform.Linux;

/// <summary>
/// Registers Linux-specific platform services.
/// Uses Replace to override stub registrations instead of adding duplicates.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxPlatformServices : IPlatformServices
{
    public static readonly LinuxPlatformServices Instance = new();

    private LinuxPlatformServices() { }

    public void Register(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IStartupService, LinuxStartupService>());
        services.Replace(ServiceDescriptor.Singleton<IWiFiInfoProvider, LinuxWiFiInfoProvider>());
        services.Replace(ServiceDescriptor.Singleton<IProcessNetworkProviderFactory, LinuxProcessNetworkProviderFactory>());
        services.Replace(ServiceDescriptor.Singleton<IElevationService, LinuxElevationService>());
        services.Replace(ServiceDescriptor.Singleton<IHelperProcessManager, LinuxHelperProcessManager>());
        services.Replace(ServiceDescriptor.Singleton<ICpuInfoProvider, LinuxCpuInfoProvider>());
        services.Replace(ServiceDescriptor.Singleton<IMemoryInfoProvider, LinuxMemoryInfoProvider>());
        services.Replace(ServiceDescriptor.Singleton<INetworkCostProvider, LinuxNetworkCostProvider>());
        services.Replace(ServiceDescriptor.Singleton<IProcessResourceProvider, LinuxProcessResourceProvider>());
    }
}

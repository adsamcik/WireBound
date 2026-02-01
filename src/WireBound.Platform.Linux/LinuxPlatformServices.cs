using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Platform.Abstract;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Linux.Services;

namespace WireBound.Platform.Linux;

/// <summary>
/// Registers Linux-specific platform services.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxPlatformServices : IPlatformServices
{
    public static readonly LinuxPlatformServices Instance = new();

    private LinuxPlatformServices() { }

    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IStartupService, LinuxStartupService>();
        services.AddSingleton<IWiFiInfoProvider, LinuxWiFiInfoProvider>();
        services.AddSingleton<IProcessNetworkProviderFactory, LinuxProcessNetworkProviderFactory>();
        services.AddSingleton<IElevationService, LinuxElevationService>();
        services.AddSingleton<ICpuInfoProvider, LinuxCpuInfoProvider>();
        services.AddSingleton<IMemoryInfoProvider, LinuxMemoryInfoProvider>();
    }
}

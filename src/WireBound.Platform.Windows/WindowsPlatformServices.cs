using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using WireBound.Platform.Abstract;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Windows.Services;

namespace WireBound.Platform.Windows;

/// <summary>
/// Registers Windows-specific platform services.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPlatformServices : IPlatformServices
{
    public static readonly WindowsPlatformServices Instance = new();
    
    private WindowsPlatformServices() { }
    
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IStartupService, WindowsStartupService>();
        services.AddSingleton<IWiFiInfoProvider, WindowsWiFiInfoProvider>();
    }
}

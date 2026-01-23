using Microsoft.Extensions.DependencyInjection;
using WireBound.Platform.Abstract;
using WireBound.Platform.Abstract.Services;
using WireBound.Platform.Stub.Services;

namespace WireBound.Platform.Stub;

/// <summary>
/// Registers stub/fallback platform services.
/// These provide no-op implementations for unsupported platforms.
/// </summary>
public sealed class StubPlatformServices : IPlatformServices
{
    public static readonly StubPlatformServices Instance = new();
    
    private StubPlatformServices() { }
    
    public void Register(IServiceCollection services)
    {
        services.AddSingleton<IStartupService, StubStartupService>();
        services.AddSingleton<IWiFiInfoProvider, StubWiFiInfoProvider>();
        services.AddSingleton<IProcessNetworkProviderFactory, StubProcessNetworkProviderFactory>();
    }
}

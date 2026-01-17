using Microsoft.Extensions.DependencyInjection;

namespace WireBound.Platform.Abstract;

/// <summary>
/// Interface for platform-specific service registration.
/// Each platform project implements this to register its services.
/// </summary>
public interface IPlatformServices
{
    /// <summary>
    /// Registers platform-specific services in the DI container.
    /// </summary>
    /// <param name="services">The service collection to register services in.</param>
    void Register(IServiceCollection services);
}

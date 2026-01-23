using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of IProcessNetworkProviderFactory.
/// Manages the Linux process network provider and handles elevation requests.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxProcessNetworkProviderFactory : IProcessNetworkProviderFactory
{
    private readonly LinuxProcessNetworkProvider _provider = new();
    private bool _hasElevatedProvider;

    public bool HasElevatedProvider => _hasElevatedProvider;

    public event EventHandler<ProviderChangedEventArgs>? ProviderChanged;

    public IProcessNetworkProvider GetProvider() => _provider;

    public Task<bool> TryElevateAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement elevation via helper process or pkexec
        // For now, we just indicate we don't have elevated capabilities
        _hasElevatedProvider = false;
        return Task.FromResult(false);
    }
}

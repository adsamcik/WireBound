using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows implementation of IProcessNetworkProviderFactory.
/// Manages the Windows process network provider and handles elevation requests.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessNetworkProviderFactory : IProcessNetworkProviderFactory
{
    private readonly WindowsProcessNetworkProvider _provider = new();
    private bool _hasElevatedProvider;

    public bool HasElevatedProvider => _hasElevatedProvider;

#pragma warning disable CS0067 // Event is never used (required by interface, future elevation support)
    public event EventHandler<ProviderChangedEventArgs>? ProviderChanged;
#pragma warning restore CS0067

    public IProcessNetworkProvider GetProvider() => _provider;

    public Task<bool> TryElevateAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Implement elevation via helper process
        // For now, we just indicate we don't have elevated capabilities
        _hasElevatedProvider = false;
        return Task.FromResult(false);
    }
}

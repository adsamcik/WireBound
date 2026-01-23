using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of IProcessNetworkProviderFactory for unsupported platforms.
/// Always returns the stub provider and cannot elevate.
/// </summary>
public sealed class StubProcessNetworkProviderFactory : IProcessNetworkProviderFactory
{
    private readonly StubProcessNetworkProvider _provider = new();

    public bool HasElevatedProvider => false;

    public event EventHandler<ProviderChangedEventArgs>? ProviderChanged;

    public IProcessNetworkProvider GetProvider() => _provider;

    public Task<bool> TryElevateAsync(CancellationToken cancellationToken = default)
    {
        // Stub cannot elevate
        return Task.FromResult(false);
    }
}

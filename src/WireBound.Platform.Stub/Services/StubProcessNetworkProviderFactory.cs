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

#pragma warning disable CS0067 // Event is never used (required by interface)
    public event EventHandler<ProviderChangedEventArgs>? ProviderChanged;
#pragma warning restore CS0067

    public IProcessNetworkProvider GetProvider() => _provider;

    public Task<bool> TryElevateAsync(CancellationToken cancellationToken = default)
    {
        // Stub cannot elevate
        return Task.FromResult(false);
    }
}

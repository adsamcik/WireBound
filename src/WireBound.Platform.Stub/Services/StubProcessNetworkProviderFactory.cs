using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Stub.Services;

/// <summary>
/// Stub implementation of IProcessNetworkProviderFactory for unsupported platforms.
/// </summary>
/// <remarks>
/// Always returns the stub provider. Elevation is not supported in stub mode.
/// </remarks>
public sealed class StubProcessNetworkProviderFactory : IProcessNetworkProviderFactory
{
    private readonly StubProcessNetworkProvider _provider = new();

    /// <inheritdoc />
    /// <remarks>Always returns false - no helper connection in stub mode.</remarks>
    public bool HasElevatedProvider => false;

#pragma warning disable CS0067 // Event is never used (required by interface)
    public event EventHandler<ProviderChangedEventArgs>? ProviderChanged;
#pragma warning restore CS0067

    /// <inheritdoc />
    public IProcessNetworkProvider GetProvider() => _provider;
}

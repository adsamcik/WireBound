using System.Runtime.Versioning;
using Serilog;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows implementation of IProcessNetworkProviderFactory.
/// </summary>
/// <remarks>
/// <para>
/// This factory creates the appropriate network provider based on helper connection state.
/// Elevation is handled by <see cref="IElevationService"/> - this factory does NOT
/// perform any elevation itself.
/// </para>
/// <para>
/// When the helper is connected, the factory can provide an elevated provider
/// that communicates with the helper via named pipes.
/// </para>
/// <para>
/// Threading: all writes to <c>_elevatedProvider</c> happen under <c>_swapGate</c>.
/// Reads use <c>volatile</c>. <see cref="ProviderChanged"/> is raised outside the
/// lock to avoid reentrancy from handlers.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessNetworkProviderFactory : IProcessNetworkProviderFactory
{
    private readonly WindowsProcessNetworkProvider _basicProvider = new();
    private readonly IElevationService? _elevationService;
    private readonly object _swapGate = new();
    private volatile IProcessNetworkProvider? _elevatedProvider;

    public WindowsProcessNetworkProviderFactory(IElevationService? elevationService = null)
    {
        _elevationService = elevationService;
        if (_elevationService != null)
        {
            _elevationService.HelperConnectionStateChanged += OnHelperConnectionStateChanged;
        }
    }

    public bool HasElevatedProvider => _elevatedProvider is not null || _elevationService?.IsHelperConnected == true;

    public event EventHandler<ProviderChangedEventArgs>? ProviderChanged;

    public IProcessNetworkProvider GetProvider()
    {
        var elevated = _elevatedProvider;
        return elevated ?? _basicProvider;
    }

    public IProcessNetworkProvider GetBasicProvider() => _basicProvider;

    private void OnHelperConnectionStateChanged(object? sender, HelperConnectionStateChangedEventArgs e)
    {
        IProcessNetworkProvider? providerToDispose = null;
        IProcessNetworkProvider providerSnapshot;

        lock (_swapGate)
        {
            if (e.IsConnected)
            {
                providerToDispose = _elevatedProvider;
                if (providerToDispose is not null)
                {
                    Log.Warning("Received duplicate helper connected event; disposing existing elevated process-network provider");
                }

                var helperConnection = _elevationService!.GetHelperConnection();
                _elevatedProvider = helperConnection is null
                    ? null
                    : new WindowsElevatedProcessNetworkProvider(helperConnection);
            }
            else
            {
                providerToDispose = _elevatedProvider;
                _elevatedProvider = null;
            }

            providerSnapshot = _elevatedProvider ?? _basicProvider;
        }

        if (providerToDispose is not null)
        {
            _ = DisposeProviderAsync(providerToDispose);
        }

        ProviderChanged?.Invoke(this, new ProviderChangedEventArgs(providerSnapshot));
    }

    private static async Task DisposeProviderAsync(IProcessNetworkProvider provider)
    {
        try
        {
            if (provider is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                provider.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error disposing replaced process-network provider");
        }
    }
}

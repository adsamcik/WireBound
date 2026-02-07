using System.Runtime.Versioning;
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
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessNetworkProviderFactory : IProcessNetworkProviderFactory
{
    private readonly WindowsProcessNetworkProvider _basicProvider = new();
    private readonly IElevationService? _elevationService;
    private IProcessNetworkProvider? _elevatedProvider;

    public WindowsProcessNetworkProviderFactory(IElevationService? elevationService = null)
    {
        _elevationService = elevationService;
        if (_elevationService != null)
        {
            _elevationService.HelperConnectionStateChanged += OnHelperConnectionStateChanged;
        }
    }

    public bool HasElevatedProvider => _elevationService?.IsHelperConnected == true;

    public event EventHandler<ProviderChangedEventArgs>? ProviderChanged;

    public IProcessNetworkProvider GetProvider()
    {
        // Return elevated provider if helper is connected, otherwise basic provider
        if (HasElevatedProvider && _elevatedProvider != null)
        {
            return _elevatedProvider;
        }
        return _basicProvider;
    }

    private void OnHelperConnectionStateChanged(object? sender, HelperConnectionStateChangedEventArgs e)
    {
        if (e.IsConnected)
        {
            var helperConnection = _elevationService!.GetHelperConnection();
            if (helperConnection is not null)
                _elevatedProvider = new WindowsElevatedProcessNetworkProvider(helperConnection);
        }
        else
        {
            _elevatedProvider?.Dispose();
            _elevatedProvider = null;
        }
        ProviderChanged?.Invoke(this, new ProviderChangedEventArgs(GetProvider()));
    }
}

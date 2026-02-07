using System.Runtime.Versioning;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of IProcessNetworkProviderFactory.
/// </summary>
/// <remarks>
/// <para>
/// This factory creates the appropriate network provider based on helper connection state.
/// Elevation is handled by <see cref="IElevationService"/> - this factory does NOT
/// perform any elevation itself.
/// </para>
/// <para>
/// When the helper is connected, the factory can provide an elevated provider
/// that communicates with the helper via Unix domain sockets.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxProcessNetworkProviderFactory : IProcessNetworkProviderFactory
{
    private readonly LinuxProcessNetworkProvider _basicProvider = new();
    private readonly IElevationService? _elevationService;
    private IProcessNetworkProvider? _elevatedProvider;

    public LinuxProcessNetworkProviderFactory(IElevationService? elevationService = null)
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
        var elevated = Volatile.Read(ref _elevatedProvider);
        if (HasElevatedProvider && elevated != null)
        {
            return elevated;
        }
        return _basicProvider;
    }

    private void OnHelperConnectionStateChanged(object? sender, HelperConnectionStateChangedEventArgs e)
    {
        if (e.IsConnected)
        {
            var helperConnection = _elevationService!.GetHelperConnection();
            if (helperConnection is not null)
                Volatile.Write(ref _elevatedProvider, new LinuxElevatedProcessNetworkProvider(helperConnection));
        }
        else
        {
            var old = Volatile.Read(ref _elevatedProvider);
            Volatile.Write(ref _elevatedProvider, null);
            old?.Dispose();
        }
        ProviderChanged?.Invoke(this, new ProviderChangedEventArgs(GetProvider()));
    }
}

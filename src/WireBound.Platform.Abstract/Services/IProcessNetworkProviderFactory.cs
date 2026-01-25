namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Factory for creating process network providers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Elevation Model:</b> This factory does NOT handle elevation directly.
/// Elevation is managed exclusively by <see cref="IElevationService"/> which
/// starts a minimal helper process. Once the helper is connected, this factory
/// can provide an elevated provider that communicates with the helper.
/// </para>
/// <para>
/// To get elevated capabilities:
/// <list type="number">
/// <item>Call <see cref="IElevationService.StartHelperAsync"/> to start the helper</item>
/// <item>Once connected, call <see cref="GetProvider"/> to get the elevated provider</item>
/// </list>
/// </para>
/// </remarks>
public interface IProcessNetworkProviderFactory
{
    /// <summary>
    /// Get the current provider based on the current helper connection state.
    /// </summary>
    /// <remarks>
    /// Returns an elevated provider if the helper is connected, otherwise
    /// returns a basic provider with limited capabilities.
    /// </remarks>
    IProcessNetworkProvider GetProvider();
    
    /// <summary>
    /// Check if an elevated provider is available (helper is connected).
    /// </summary>
    bool HasElevatedProvider { get; }
    
    /// <summary>
    /// Event raised when the provider changes (e.g., when helper connects/disconnects).
    /// </summary>
    event EventHandler<ProviderChangedEventArgs>? ProviderChanged;
}

/// <summary>
/// Event args for provider change events.
/// </summary>
public class ProviderChangedEventArgs : EventArgs
{
    /// <summary>
    /// The new provider that is now active.
    /// </summary>
    public IProcessNetworkProvider NewProvider { get; }
    
    /// <summary>
    /// The capabilities of the new provider.
    /// </summary>
    public ProcessNetworkCapabilities NewCapabilities { get; }
    
    public ProviderChangedEventArgs(IProcessNetworkProvider newProvider)
    {
        NewProvider = newProvider;
        NewCapabilities = newProvider.Capabilities;
    }
}

namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Factory for creating process network providers.
/// Handles runtime switching between elevated and non-elevated providers.
/// </summary>
public interface IProcessNetworkProviderFactory
{
    /// <summary>
    /// Get the current provider based on the current elevation state.
    /// </summary>
    IProcessNetworkProvider GetProvider();
    
    /// <summary>
    /// Check if an elevated provider is available.
    /// </summary>
    bool HasElevatedProvider { get; }
    
    /// <summary>
    /// Attempt to switch to an elevated provider.
    /// This may trigger a UAC prompt on Windows or sudo on Linux.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if elevation succeeded and provider was switched</returns>
    Task<bool> TryElevateAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Event raised when the provider changes (e.g., after elevation).
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

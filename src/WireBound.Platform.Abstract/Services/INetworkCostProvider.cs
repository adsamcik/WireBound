namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Platform-specific provider for network cost/metered status.
/// Used to skip auto-download on metered/limited connections.
/// </summary>
public interface INetworkCostProvider
{
    /// <summary>
    /// Returns true if the current network connection appears to be metered or limited.
    /// </summary>
    Task<bool> IsMeteredAsync();
}

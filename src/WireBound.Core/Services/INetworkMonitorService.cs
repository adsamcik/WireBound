using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Interface for network monitoring service
/// </summary>
public interface INetworkMonitorService
{
    /// <summary>
    /// Event raised when new statistics are available
    /// </summary>
    event EventHandler<NetworkStats>? StatsUpdated;

    /// <summary>
    /// Get network adapters. By default returns only physical adapters (simple mode).
    /// </summary>
    /// <param name="includeVirtual">If true, includes virtual adapters (VPN, Hyper-V, etc.)</param>
    IReadOnlyList<NetworkAdapter> GetAdapters(bool includeVirtual = false);

    /// <summary>
    /// Get current network statistics
    /// </summary>
    NetworkStats GetCurrentStats();

    /// <summary>
    /// Get stats for a specific adapter
    /// </summary>
    NetworkStats GetStats(string adapterId);
    
    /// <summary>
    /// Get current stats for all active adapters
    /// </summary>
    IReadOnlyDictionary<string, NetworkStats> GetAllAdapterStats();

    /// <summary>
    /// Set the adapter to monitor (empty = aggregate all)
    /// </summary>
    void SetAdapter(string adapterId);

    /// <summary>
    /// Whether the service is using IP Helper API for monitoring (Windows-specific)
    /// </summary>
    bool IsUsingIpHelperApi { get; }

    /// <summary>
    /// Enable or disable IP Helper API mode (Windows-specific)
    /// </summary>
    void SetUseIpHelperApi(bool useIpHelper);

    /// <summary>
    /// Perform a single poll and update stats
    /// </summary>
    void Poll();

    /// <summary>
    /// Reset the current session counters
    /// </summary>
    void ResetSession();
}

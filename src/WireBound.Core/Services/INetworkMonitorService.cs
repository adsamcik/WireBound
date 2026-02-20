using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Sentinel value for auto-detect adapter mode.
/// When set as the selected adapter ID, the service automatically resolves
/// the primary internet adapter via default gateway detection.
/// </summary>
public static class NetworkMonitorConstants
{
    public const string AutoAdapterId = "auto";
}

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
    /// Set the adapter to monitor.
    /// Use "auto" for automatic primary detection via default gateway,
    /// empty string for aggregate all, or a specific adapter ID.
    /// </summary>
    void SetAdapter(string adapterId);

    /// <summary>
    /// Gets the ID of the primary internet adapter detected via default gateway.
    /// Returns empty string if no gateway adapter is found.
    /// </summary>
    string GetPrimaryAdapterId();

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

using WireBound.Maui.Models;

namespace WireBound.Maui.Services;

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
    /// Get all available network adapters
    /// </summary>
    IReadOnlyList<NetworkAdapter> GetAdapters();

    /// <summary>
    /// Get current network statistics
    /// </summary>
    NetworkStats GetCurrentStats();

    /// <summary>
    /// Get stats for a specific adapter
    /// </summary>
    NetworkStats GetStats(string adapterId);

    /// <summary>
    /// Set the adapter to monitor (empty = aggregate all)
    /// </summary>
    void SetAdapter(string adapterId);

    /// <summary>
    /// Whether the service is using IP Helper API fallback
    /// </summary>
    bool IsUsingIpHelperApi { get; }

    /// <summary>
    /// Enable or disable IP Helper API mode
    /// </summary>
    void SetUseIpHelperApi(bool useIpHelper);

    /// <summary>
    /// Perform a single poll and update stats
    /// </summary>
    void Poll();

    /// <summary>
    /// Reset session counters
    /// </summary>
    void ResetSession();
}

using WireBound.Core.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Interface for system resource monitoring (CPU, Memory)
/// </summary>
public interface ISystemMonitorService
{
    /// <summary>
    /// Event raised when new system statistics are available
    /// </summary>
    event EventHandler<SystemStats>? StatsUpdated;

    /// <summary>
    /// Get current system statistics
    /// </summary>
    SystemStats GetCurrentStats();

    /// <summary>
    /// Poll for new statistics and raise StatsUpdated event
    /// </summary>
    void Poll();

    /// <summary>
    /// Get the processor name/model
    /// </summary>
    string GetProcessorName();

    /// <summary>
    /// Get the number of logical processors
    /// </summary>
    int GetProcessorCount();

    /// <summary>
    /// Whether CPU temperature monitoring is available
    /// </summary>
    bool IsCpuTemperatureAvailable { get; }

    /// <summary>
    /// Whether per-core CPU usage is available
    /// </summary>
    bool IsPerCoreUsageAvailable { get; }
}

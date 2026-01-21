using WireBound.Core.Models;
using WireBound.Platform.Abstract.Models;

namespace WireBound.Core.Services;

/// <summary>
/// Service for monitoring per-process network statistics.
/// This is an optional feature that may require elevated privileges.
/// </summary>
public interface IProcessNetworkService : IDisposable
{
    /// <summary>
    /// Whether the service is currently running and collecting data
    /// </summary>
    bool IsRunning { get; }
    
    /// <summary>
    /// Whether the current process has the required privileges for network tracing
    /// </summary>
    bool HasRequiredPrivileges { get; }
    
    /// <summary>
    /// Whether per-app tracking is supported on this platform
    /// </summary>
    bool IsPlatformSupported { get; }
    
    /// <summary>
    /// Start collecting per-process network statistics.
    /// May require elevation on some platforms.
    /// </summary>
    /// <returns>True if started successfully, false otherwise</returns>
    Task<bool> StartAsync();
    
    /// <summary>
    /// Stop collecting per-process network statistics
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// Get the current snapshot of all process network statistics
    /// </summary>
    IReadOnlyList<ProcessNetworkStats> GetCurrentStats();
    
    /// <summary>
    /// Get the top N processes by current bandwidth usage
    /// </summary>
    /// <param name="count">Number of top processes to return</param>
    IReadOnlyList<ProcessNetworkStats> GetTopProcesses(int count);
    
    /// <summary>
    /// Raised when process statistics are updated
    /// </summary>
    event EventHandler<ProcessStatsUpdatedEventArgs>? StatsUpdated;
    
    /// <summary>
    /// Raised when the service encounters an error
    /// </summary>
    event EventHandler<ProcessNetworkErrorEventArgs>? ErrorOccurred;
}

/// <summary>
/// Event args for process statistics updates
/// </summary>
public class ProcessStatsUpdatedEventArgs : EventArgs
{
    public IReadOnlyList<ProcessNetworkStats> Stats { get; }
    public DateTime Timestamp { get; }
    
    public ProcessStatsUpdatedEventArgs(IReadOnlyList<ProcessNetworkStats> stats)
    {
        Stats = stats;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// Event args for process network service errors
/// </summary>
public class ProcessNetworkErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }
    public bool RequiresElevation { get; }
    
    public ProcessNetworkErrorEventArgs(string message, Exception? exception = null, bool requiresElevation = false)
    {
        Message = message;
        Exception = exception;
        RequiresElevation = requiresElevation;
    }
}

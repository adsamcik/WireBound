using WireBound.Platform.Abstract.Models;

namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Low-level platform abstraction for per-process network traffic monitoring.
/// This is the platform-specific implementation that IProcessNetworkService uses.
/// </summary>
public interface IProcessNetworkProvider : IDisposable
{
    /// <summary>
    /// Capabilities of this provider on the current platform and privilege level.
    /// </summary>
    ProcessNetworkCapabilities Capabilities { get; }

    /// <summary>
    /// Whether the provider is currently actively monitoring.
    /// </summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// Start monitoring network traffic per process.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if monitoring started successfully</returns>
    Task<bool> StartMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop monitoring and release resources.
    /// </summary>
    Task StopMonitoringAsync();

    /// <summary>
    /// Get current snapshot of per-process network statistics.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of process network stats, empty if not monitoring</returns>
    Task<IReadOnlyList<ProcessNetworkStats>> GetProcessStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active connections from the OS connection tables.
    /// Available without elevation on all platforms.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active connections</returns>
    Task<IReadOnlyList<ConnectionInfo>> GetActiveConnectionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get connection statistics with byte counters.
    /// Byte counters only populated if Capabilities includes ByteCounters.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of connection stats, with byte counters if available</returns>
    Task<IReadOnlyList<ConnectionStats>> GetConnectionStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when new statistics are available.
    /// Frequency depends on implementation (polling interval or event-driven).
    /// </summary>
    event EventHandler<ProcessNetworkProviderEventArgs>? StatsUpdated;

    /// <summary>
    /// Event raised when an error occurs during monitoring.
    /// </summary>
    event EventHandler<ProcessNetworkProviderErrorEventArgs>? ErrorOccurred;
}

/// <summary>
/// Capabilities flags indicating what the provider can do.
/// Based on platform support and current privilege level.
/// </summary>
[Flags]
public enum ProcessNetworkCapabilities
{
    /// <summary>
    /// Provider has no capabilities (unsupported platform or insufficient privileges)
    /// </summary>
    None = 0,

    /// <summary>
    /// Can enumerate active network connections per process
    /// </summary>
    ConnectionList = 1,

    /// <summary>
    /// Can track bytes sent/received per process
    /// </summary>
    ByteCounters = 2,

    /// <summary>
    /// Can calculate real-time bandwidth per process
    /// </summary>
    RealTimeBandwidth = 4,

    /// <summary>
    /// Full byte counter features require elevation (admin/root)
    /// </summary>
    RequiresElevation = 8,

    /// <summary>
    /// Provider supports historical data persistence
    /// </summary>
    SupportsHistorical = 16,

    /// <summary>
    /// Provider is using an elevated helper process
    /// </summary>
    UsingElevatedHelper = 32
}

/// <summary>
/// Event args for process network statistics updates from provider.
/// </summary>
public class ProcessNetworkProviderEventArgs : EventArgs
{
    /// <summary>
    /// The updated process statistics.
    /// </summary>
    public IReadOnlyList<ProcessNetworkStats> Stats { get; }

    /// <summary>
    /// When the stats were collected.
    /// </summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// Time interval these stats represent (for bandwidth calculation).
    /// </summary>
    public TimeSpan Interval { get; }

    public ProcessNetworkProviderEventArgs(
        IReadOnlyList<ProcessNetworkStats> stats,
        DateTimeOffset timestamp,
        TimeSpan interval)
    {
        Stats = stats;
        Timestamp = timestamp;
        Interval = interval;
    }
}

/// <summary>
/// Event args for provider errors.
/// </summary>
public class ProcessNetworkProviderErrorEventArgs : EventArgs
{
    /// <summary>
    /// Error message describing what went wrong.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// The exception that caused the error, if any.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Whether the error is recoverable (monitoring can continue).
    /// </summary>
    public bool IsRecoverable { get; }

    public ProcessNetworkProviderErrorEventArgs(string message, Exception? exception = null, bool isRecoverable = true)
    {
        Message = message;
        Exception = exception;
        IsRecoverable = isRecoverable;
    }
}

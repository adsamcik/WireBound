namespace WireBound.Platform.Abstract.Models;

/// <summary>
/// Real-time network statistics for a single process
/// </summary>
public class ProcessNetworkStats
{
    /// <summary>
    /// Process ID
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Process name (e.g., "chrome")
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Full path to the executable
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// SHA256 hash of the executable path (stable identifier)
    /// </summary>
    public string AppIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// Friendly display name for the application
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Current download speed in bytes per second
    /// </summary>
    public long DownloadSpeedBps { get; set; }

    /// <summary>
    /// Current upload speed in bytes per second
    /// </summary>
    public long UploadSpeedBps { get; set; }

    /// <summary>
    /// Total bytes received this session
    /// </summary>
    public long SessionBytesReceived { get; set; }

    /// <summary>
    /// Total bytes sent this session
    /// </summary>
    public long SessionBytesSent { get; set; }

    /// <summary>
    /// Subset of <see cref="SessionBytesReceived"/> that came from loopback /
    /// localhost remote addresses (127.0.0.0/8, ::1). Zero unless the elevated
    /// helper provided per-connection classification.
    /// </summary>
    public long LoopbackBytesReceived { get; set; }

    /// <summary>
    /// Subset of <see cref="SessionBytesSent"/> that went to loopback / localhost.
    /// </summary>
    public long LoopbackBytesSent { get; set; }

    /// <summary>
    /// When this process was first seen in current session
    /// </summary>
    public DateTime FirstSeen { get; set; } = DateTime.Now;

    /// <summary>
    /// When this process was last active
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.Now;

    /// <summary>
    /// Application icon as base64 (for cross-platform)
    /// </summary>
    public string? IconBase64 { get; set; }

    /// <summary>
    /// Combined current speed (download + upload)
    /// </summary>
    public long TotalSpeedBps => DownloadSpeedBps + UploadSpeedBps;

    /// <summary>
    /// Combined session bytes (received + sent)
    /// </summary>
    public long TotalSessionBytes => SessionBytesReceived + SessionBytesSent;

    /// <summary>
    /// Network (non-loopback) bytes received this session.
    /// </summary>
    public long NetworkBytesReceived => Math.Max(0, SessionBytesReceived - LoopbackBytesReceived);

    /// <summary>
    /// Network (non-loopback) bytes sent this session.
    /// </summary>
    public long NetworkBytesSent => Math.Max(0, SessionBytesSent - LoopbackBytesSent);
}

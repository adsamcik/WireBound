namespace WireBound.Core.Models;

/// <summary>
/// Network statistics for a specific connection.
/// Extends ConnectionInfo with byte counters and speed tracking.
/// Full byte tracking requires elevated helper process.
/// </summary>
public class ConnectionStats
{
    /// <summary>
    /// Local IP address (IPv4 or IPv6)
    /// </summary>
    public string LocalAddress { get; set; } = string.Empty;

    /// <summary>
    /// Local port number
    /// </summary>
    public int LocalPort { get; set; }

    /// <summary>
    /// Remote IP address (IPv4 or IPv6)
    /// </summary>
    public string RemoteAddress { get; set; } = string.Empty;

    /// <summary>
    /// Remote port number
    /// </summary>
    public int RemotePort { get; set; }

    /// <summary>
    /// Process ID that owns this connection
    /// </summary>
    public int ProcessId { get; set; }

    /// <summary>
    /// Process name (cached for display)
    /// </summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>
    /// Protocol type (TCP or UDP)
    /// </summary>
    public string Protocol { get; set; } = "TCP";

    /// <summary>
    /// Current state of the connection
    /// </summary>
    public ConnectionState State { get; set; } = ConnectionState.Unknown;

    /// <summary>
    /// Resolved hostname for the remote address (cached, may be null)
    /// </summary>
    public string? ResolvedHostname { get; set; }

    // === Byte Counters (requires elevation) ===

    /// <summary>
    /// Total bytes sent on this connection
    /// </summary>
    public long BytesSent { get; set; }

    /// <summary>
    /// Total bytes received on this connection
    /// </summary>
    public long BytesReceived { get; set; }

    /// <summary>
    /// Whether byte counters are available (false if not elevated)
    /// </summary>
    public bool HasByteCounters { get; set; }

    // === Speed Tracking ===

    /// <summary>
    /// Current send speed in bytes per second
    /// </summary>
    public long SendSpeedBps { get; set; }

    /// <summary>
    /// Current receive speed in bytes per second
    /// </summary>
    public long ReceiveSpeedBps { get; set; }

    // === Timing ===

    /// <summary>
    /// When this connection was first observed
    /// </summary>
    public DateTime FirstSeen { get; set; } = DateTime.Now;

    /// <summary>
    /// When traffic was last detected on this connection
    /// </summary>
    public DateTime LastActivity { get; set; } = DateTime.Now;

    // === Computed Properties ===

    /// <summary>
    /// Total bytes transferred (sent + received)
    /// </summary>
    public long TotalBytes => BytesSent + BytesReceived;

    /// <summary>
    /// Total current speed (send + receive)
    /// </summary>
    public long TotalSpeedBps => SendSpeedBps + ReceiveSpeedBps;

    /// <summary>
    /// Whether this is an IPv6 connection
    /// </summary>
    public bool IsIPv6 => LocalAddress.Contains(':');

    /// <summary>
    /// Unique key for this connection tuple
    /// </summary>
    public string ConnectionKey => $"{Protocol}:{LocalAddress}:{LocalPort}->{RemoteAddress}:{RemotePort}";

    /// <summary>
    /// Display name (hostname if resolved, otherwise IP)
    /// </summary>
    public string DisplayName => ResolvedHostname ?? RemoteAddress;

    /// <summary>
    /// Remote endpoint for display
    /// </summary>
    public string RemoteEndpoint => RemotePort > 0
        ? $"{DisplayName}:{RemotePort}"
        : DisplayName;

    /// <summary>
    /// Creates a ConnectionStats from a ConnectionInfo
    /// </summary>
    public static ConnectionStats FromConnectionInfo(ConnectionInfo info)
    {
        return new ConnectionStats
        {
            LocalAddress = info.LocalAddress,
            LocalPort = info.LocalPort,
            RemoteAddress = info.RemoteAddress,
            RemotePort = info.RemotePort,
            ProcessId = info.ProcessId,
            Protocol = info.Protocol,
            State = info.State,
            FirstSeen = info.FirstSeen,
            HasByteCounters = false
        };
    }
}

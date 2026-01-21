namespace WireBound.Core.Models;

/// <summary>
/// Represents the state of a TCP connection.
/// Maps to MIB_TCP_STATE on Windows and states in /proc/net/tcp on Linux.
/// </summary>
public enum ConnectionState
{
    Unknown = 0,
    Closed = 1,
    Listen = 2,
    SynSent = 3,
    SynReceived = 4,
    Established = 5,
    FinWait1 = 6,
    FinWait2 = 7,
    CloseWait = 8,
    Closing = 9,
    LastAck = 10,
    TimeWait = 11,
    DeleteTcb = 12
}

/// <summary>
/// Represents an active network connection from the OS connection table.
/// Available without elevation on all platforms.
/// </summary>
public class ConnectionInfo
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
    /// Protocol type (TCP or UDP)
    /// </summary>
    public string Protocol { get; set; } = "TCP";
    
    /// <summary>
    /// Current state of the connection (TCP only)
    /// </summary>
    public ConnectionState State { get; set; } = ConnectionState.Unknown;
    
    /// <summary>
    /// When this connection was first observed
    /// </summary>
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Whether this is an IPv6 connection
    /// </summary>
    public bool IsIPv6 => LocalAddress.Contains(':');
    
    /// <summary>
    /// Unique key for this connection tuple
    /// </summary>
    public string ConnectionKey => $"{Protocol}:{LocalAddress}:{LocalPort}->{RemoteAddress}:{RemotePort}";
    
    /// <summary>
    /// Creates a display-friendly description of the remote endpoint
    /// </summary>
    public string RemoteEndpoint => RemotePort > 0 
        ? $"{RemoteAddress}:{RemotePort}" 
        : RemoteAddress;
        
    /// <summary>
    /// Creates a display-friendly description of the local endpoint
    /// </summary>
    public string LocalEndpoint => $"{LocalAddress}:{LocalPort}";
}

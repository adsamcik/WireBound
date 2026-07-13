using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class ProcessStatsResponse
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public List<ProcessByteStats> Processes { get; set; } = [];

    [Key(2)]
    public string? ErrorMessage { get; set; }
}

[MessagePackObject]
public class ProcessByteStats
{
    [Key(0)]
    public int ProcessId { get; set; }

    [Key(1)]
    public string ProcessName { get; set; } = string.Empty;

    [Key(2)]
    public long TotalBytesSent { get; set; }

    [Key(3)]
    public long TotalBytesReceived { get; set; }

    [Key(4)]
    public int ActiveConnectionCount { get; set; }

    /// <summary>
    /// Fully qualified executable path for the process, when the helper can
    /// resolve it. Required by the app-side consumer to derive a stable
    /// <c>AppIdentifier</c> and to surface a friendly display name.
    /// Empty when the helper failed to query the process (e.g. PID has
    /// exited or is a protected system process).
    /// </summary>
    [Key(5)]
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Subset of <see cref="TotalBytesSent"/> that went to loopback/localhost
    /// remote addresses (127.0.0.0/8, ::1). Lets the app separate real network
    /// traffic from local IPC-style traffic (e.g. adb, dev servers) in per-app
    /// accounting. Computed by the helper from its per-connection data.
    /// </summary>
    [Key(6)]
    public long LoopbackBytesSent { get; set; }

    /// <summary>
    /// Subset of <see cref="TotalBytesReceived"/> received from loopback/localhost
    /// remote addresses. See <see cref="LoopbackBytesSent"/>.
    /// </summary>
    [Key(7)]
    public long LoopbackBytesReceived { get; set; }
}

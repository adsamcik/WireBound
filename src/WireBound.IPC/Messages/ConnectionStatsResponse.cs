using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class ConnectionStatsResponse
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public List<ProcessConnectionStats> Processes { get; set; } = [];

    [Key(2)]
    public string? ErrorMessage { get; set; }
}

[MessagePackObject]
public class ProcessConnectionStats
{
    [Key(0)]
    public int ProcessId { get; set; }

    [Key(1)]
    public string ProcessName { get; set; } = string.Empty;

    [Key(2)]
    public long BytesSent { get; set; }

    [Key(3)]
    public long BytesReceived { get; set; }

    [Key(4)]
    public List<ConnectionByteStats> Connections { get; set; } = [];

    /// <summary>
    /// Fully qualified executable path for the process when the helper can
    /// resolve it. Empty for protected/exited processes. Consumers derive
    /// stable application identity (<c>AppIdentifier</c>) from this value.
    /// </summary>
    [Key(5)]
    public string ExecutablePath { get; set; } = string.Empty;
}

[MessagePackObject]
public class ConnectionByteStats
{
    [Key(0)]
    public string LocalAddress { get; set; } = string.Empty;

    [Key(1)]
    public int LocalPort { get; set; }

    [Key(2)]
    public string RemoteAddress { get; set; } = string.Empty;

    [Key(3)]
    public int RemotePort { get; set; }

    [Key(4)]
    public byte Protocol { get; set; } // 6 = TCP, 17 = UDP

    [Key(5)]
    public long BytesSent { get; set; }

    [Key(6)]
    public long BytesReceived { get; set; }
}

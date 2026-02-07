namespace WireBound.IPC.Messages;

public class ConnectionStatsResponse
{
    public bool Success { get; set; }
    public List<ProcessConnectionStats> Processes { get; set; } = [];
    public string? ErrorMessage { get; set; }
}

public class ProcessConnectionStats
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public List<ConnectionByteStats> Connections { get; set; } = [];
}

public class ConnectionByteStats
{
    public string LocalEndpoint { get; set; } = string.Empty;
    public string RemoteEndpoint { get; set; } = string.Empty;
    public string Protocol { get; set; } = "TCP";
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
}

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
}

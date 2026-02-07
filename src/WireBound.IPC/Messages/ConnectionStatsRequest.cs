using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class ConnectionStatsRequest
{
    [Key(0)]
    public string SessionId { get; set; } = string.Empty;
}

using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class HeartbeatResponse
{
    [Key(0)]
    public bool Alive { get; set; }

    [Key(1)]
    public long UptimeSeconds { get; set; }

    [Key(2)]
    public int ActiveSessions { get; set; }
}

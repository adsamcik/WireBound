namespace WireBound.IPC.Messages;

public class HeartbeatResponse
{
    public bool Alive { get; set; }
    public TimeSpan Uptime { get; set; }
    public int ActiveSessions { get; set; }
}

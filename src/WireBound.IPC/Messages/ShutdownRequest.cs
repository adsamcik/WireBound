namespace WireBound.IPC.Messages;

public class ShutdownRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string Reason { get; set; } = "Client requested shutdown";
}

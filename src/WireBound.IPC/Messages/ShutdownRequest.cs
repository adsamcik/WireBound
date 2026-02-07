using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class ShutdownRequest
{
    [Key(0)]
    public string SessionId { get; set; } = string.Empty;

    [Key(1)]
    public string Reason { get; set; } = "Client requested shutdown";
}

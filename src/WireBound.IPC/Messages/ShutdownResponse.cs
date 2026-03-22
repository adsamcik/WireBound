using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class ShutdownResponse
{
    [Key(0)]
    public bool Acknowledged { get; set; }

    [Key(1)]
    public string Reason { get; set; } = string.Empty;
}

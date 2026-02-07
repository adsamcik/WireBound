using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class AuthenticateRequest
{
    [Key(0)]
    public int ClientPid { get; set; }

    [Key(1)]
    public long Timestamp { get; set; }

    [Key(2)]
    public string Signature { get; set; } = string.Empty;

    [Key(3)]
    public string ExecutablePath { get; set; } = string.Empty;
}

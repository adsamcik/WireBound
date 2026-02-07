using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class AuthenticateResponse
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public string? SessionId { get; set; }

    [Key(2)]
    public string? ErrorMessage { get; set; }

    [Key(3)]
    public long ExpiresAtUtc { get; set; }
}

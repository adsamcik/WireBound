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

    /// <summary>
    /// Server's HMAC over (Nonce || SessionId || ExpiresAtUtc) proving the
    /// server holds the same secret and that this response is bound to the
    /// client's specific challenge — defeats pipe-squatting and replay.
    /// </summary>
    [Key(4)]
    public string ServerSignature { get; set; } = string.Empty;
}

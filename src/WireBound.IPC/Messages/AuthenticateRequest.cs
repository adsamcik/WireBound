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

    /// <summary>
    /// SHA-256 hash of the client's own executable image. The server
    /// independently recomputes this from the verified executable on disk
    /// and compares with FixedTimeEquals. Defeats T7 (off-path copies)
    /// and any same-name binary planted in a writable directory.
    /// </summary>
    [Key(4)]
    public byte[] ClientImageHash { get; set; } = [];

    /// <summary>
    /// Nonce echoed back from the server's Challenge message. Replaces
    /// timestamp-based freshness with a single-use server-issued nonce
    /// so captured signatures cannot be replayed within or across boots.
    /// </summary>
    [Key(5)]
    public byte[] Nonce { get; set; } = [];
}

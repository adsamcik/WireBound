using MessagePack;

namespace WireBound.IPC.Messages;

/// <summary>
/// Base envelope for all IPC messages.
/// Uses integer keys for compact binary serialization.
/// </summary>
[MessagePackObject]
public class IpcMessage
{
    [Key(0)]
    public MessageType Type { get; set; }

    [Key(1)]
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    [Key(2)]
    public byte[] Payload { get; set; } = [];
}

public enum MessageType : byte
{
    Authenticate = 1,
    ConnectionStats = 2,
    ProcessStats = 3,
    Heartbeat = 4,
    Shutdown = 5,
    Error = 255
}

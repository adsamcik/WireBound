using MessagePack;

namespace WireBound.IPC.Messages;

/// <summary>
/// Base envelope for all IPC messages.
/// Uses integer keys for compact binary serialization.
/// </summary>
/// <remarks>
/// <para>Schema-evolution rules for every <see cref="MessagePack.MessagePackObjectAttribute"/>
/// type in <see cref="WireBound.IPC.Messages"/>:</para>
/// <list type="bullet">
///   <item>Keys MUST stay dense (0..N). If you remove a property, leave its
///         <c>[Key(n)]</c> slot reserved with a placeholder — the formatter
///         serializes as a fixed-length array and any reordering silently
///         misaligns every downstream field.</item>
///   <item>Adding a new property at <c>[Key(N+1)]</c> is safe. Newer servers
///         talking to older clients will have the trailing field skipped;
///         newer clients talking to older servers will see the default.</item>
///   <item>Never change the wire type of an existing key.</item>
///   <item>Both the helper and the main app must ship the SAME
///         <c>WireBound.IPC.dll</c>. A mismatched copy in either output
///         directory will deserialize successfully but with missing/default
///         fields — silent data loss.</item>
/// </list>
/// </remarks>
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

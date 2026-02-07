using MessagePack;
using WireBound.IPC.Messages;

namespace WireBound.IPC.Transport;

/// <summary>
/// Handles sending and receiving IPC messages over a stream.
/// Uses length-prefixed MessagePack framing: [4-byte big-endian length][MessagePack payload]
/// </summary>
public static class IpcTransport
{
    public static async Task SendAsync(Stream stream, IpcMessage message, CancellationToken ct = default)
    {
        var bytes = MessagePackSerializer.Serialize(message, cancellationToken: ct);
        if (bytes.Length > IpcConstants.MaxMessageSize)
            throw new InvalidOperationException($"Message exceeds max size: {bytes.Length}");

        var length = BitConverter.GetBytes(bytes.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(length);

        await stream.WriteAsync(length, ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<IpcMessage?> ReceiveAsync(Stream stream, CancellationToken ct = default)
    {
        var lengthBuffer = new byte[4];
        var bytesRead = await ReadExactAsync(stream, lengthBuffer, ct);
        if (bytesRead < 4) return null;

        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBuffer);
        var length = BitConverter.ToInt32(lengthBuffer, 0);

        if (length <= 0 || length > IpcConstants.MaxMessageSize) return null;

        var buffer = new byte[length];
        bytesRead = await ReadExactAsync(stream, buffer, ct);
        if (bytesRead < length) return null;

        return MessagePackSerializer.Deserialize<IpcMessage>(buffer, cancellationToken: ct);
    }

    /// <summary>
    /// Serializes a payload DTO into the MessagePack binary format for embedding in IpcMessage.Payload.
    /// </summary>
    public static byte[] SerializePayload<T>(T payload) =>
        MessagePackSerializer.Serialize(payload);

    /// <summary>
    /// Deserializes a payload DTO from the MessagePack binary stored in IpcMessage.Payload.
    /// </summary>
    public static T DeserializePayload<T>(byte[] payload) =>
        MessagePackSerializer.Deserialize<T>(payload);

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0) return totalRead;
            totalRead += read;
        }
        return totalRead;
    }
}

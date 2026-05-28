using System.Buffers.Binary;
using MessagePack;
using WireBound.IPC.Messages;

namespace WireBound.IPC.Transport;

/// <summary>
/// Handles sending and receiving IPC messages over a stream.
/// Uses length-prefixed MessagePack framing: [4-byte big-endian length][MessagePack payload].
/// </summary>
/// <remarks>
/// Every public transport method either succeeds, returns gracefully, or throws.
/// <see cref="ReceiveAsync"/> no longer uses a <see langword="null"/> <see cref="IpcMessage"/>
/// to signal framing failure; framing failures throw <see cref="IpcFramingException"/>.
/// Callers must dispose the underlying stream on <see cref="IpcFramingException"/> because
/// the stream pointer may be mid-frame and cannot be safely resynchronized.
/// </remarks>
public static class IpcTransport
{
    /// <summary>
    /// Secure MessagePack options using source-generated resolver for AOT compatibility,
    /// with UntrustedData protection against hash-flooding and deserialization attacks.
    /// The composite resolver combines our generated formatters with standard built-in formatters.
    /// </summary>
    private static readonly MessagePackSerializerOptions SecureOptions =
        MessagePackSerializerOptions.Standard
            .WithResolver(
                MessagePack.Resolvers.CompositeResolver.Create(
                    WireBoundIpcResolver.Instance,
                    MessagePack.Resolvers.StandardResolver.Instance))
            .WithSecurity(MessagePackSecurity.UntrustedData);

    private static readonly TimeSpan DefaultReceiveTimeout = TimeSpan.FromSeconds(30);

    public static async Task SendAsync(Stream stream, IpcMessage message, CancellationToken ct = default)
    {
        var bytes = MessagePackSerializer.Serialize(message, SecureOptions, ct);
        if (bytes.Length > IpcConstants.MaxMessageSize)
            throw new InvalidOperationException($"Message exceeds max size: {bytes.Length}");

        var frame = new byte[sizeof(int) + bytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, sizeof(int)), bytes.Length);
        bytes.CopyTo(frame.AsSpan(sizeof(int)));

        await stream.WriteAsync(frame, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<IpcMessage> ReceiveAsync(
        Stream stream,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? DefaultReceiveTimeout;
        CancellationTokenSource? timeoutCts = null;
        var readToken = ct;

        if (effectiveTimeout != TimeSpan.Zero && effectiveTimeout != Timeout.InfiniteTimeSpan)
        {
            timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(effectiveTimeout);
            readToken = timeoutCts.Token;
        }

        try
        {
            var lengthBuffer = new byte[sizeof(int)];
            try
            {
                await ReadExactAsync(stream, lengthBuffer, readToken);
            }
            catch (EndOfStreamException ex)
            {
                throw new IpcFramingException("Short read while reading IPC message length prefix.", ex);
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
            if (length <= 0 || length > IpcConstants.MaxMessageSize)
                throw new IpcFramingException($"Invalid IPC message length: {length}.");

            var buffer = new byte[length];
            try
            {
                await ReadExactAsync(stream, buffer, readToken);
            }
            catch (EndOfStreamException ex)
            {
                throw new IpcFramingException($"Short read while reading IPC payload: expected {length} bytes.", ex);
            }

            try
            {
                return MessagePackSerializer.Deserialize<IpcMessage>(buffer, SecureOptions, readToken);
            }
            catch (MessagePackSerializationException ex)
            {
                throw new IpcFramingException("Failed to deserialize IPC MessagePack frame.", ex);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new IpcFramingException($"Timed out while reading IPC frame after {effectiveTimeout}.", ex);
        }
        finally
        {
            timeoutCts?.Dispose();
        }
    }

    /// <summary>
    /// Serializes a payload DTO into the MessagePack binary format for embedding in IpcMessage.Payload.
    /// </summary>
    public static byte[] SerializePayload<T>(T payload) =>
        MessagePackSerializer.Serialize(payload, SecureOptions);

    /// <summary>
    /// Deserializes a payload DTO from the MessagePack binary stored in IpcMessage.Payload.
    /// </summary>
    public static T DeserializePayload<T>(byte[] payload) =>
        MessagePackSerializer.Deserialize<T>(payload, SecureOptions);

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0)
                throw new EndOfStreamException($"Stream ended after {totalRead} of {buffer.Length} bytes.");

            totalRead += read;
        }
    }
}

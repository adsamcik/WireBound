using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

/// <summary>
/// Additional edge case tests for IpcTransport — partial reads, corruption,
/// boundary sizes, concurrent access.
/// </summary>
public class IpcTransportEdgeCaseTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Boundary message sizes
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SendAsync_ExactlyMaxMessageSize_Throws()
    {
        // A payload that serializes to > MaxMessageSize should throw
        var hugePayload = new byte[IpcConstants.MaxMessageSize + 1];
        var msg = new IpcMessage
        {
            Type = MessageType.Error,
            RequestId = "x",
            Payload = hugePayload
        };

        using var ms = new MemoryStream();
        var act = () => IpcTransport.SendAsync(ms, msg).GetAwaiter().GetResult();
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Contain("max size");
    }

    [Test]
    public void ReceiveAsync_NegativeLength_ReturnsNull()
    {
        using var ms = new MemoryStream();
        // Write a negative length in big-endian
        var lengthBytes = BitConverter.GetBytes(-1);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        ms.Write(lengthBytes);
        ms.Position = 0;

        var result = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        result.Should().BeNull();
    }

    [Test]
    public void ReceiveAsync_ZeroLength_ReturnsNull()
    {
        using var ms = new MemoryStream();
        var lengthBytes = BitConverter.GetBytes(0);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        ms.Write(lengthBytes);
        ms.Position = 0;

        var result = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        result.Should().BeNull();
    }

    [Test]
    public void ReceiveAsync_LengthExceedsMax_ReturnsNull()
    {
        using var ms = new MemoryStream();
        var length = IpcConstants.MaxMessageSize + 1;
        var lengthBytes = BitConverter.GetBytes(length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        ms.Write(lengthBytes);
        ms.Position = 0;

        var result = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Partial reads / stream closes
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ReceiveAsync_StreamClosedBeforeLength_ReturnsNull()
    {
        using var ms = new MemoryStream([0x00, 0x00]); // Only 2 bytes, need 4
        var result = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        result.Should().BeNull();
    }

    [Test]
    public void ReceiveAsync_StreamClosedDuringPayload_ReturnsNull()
    {
        using var ms = new MemoryStream();
        // Write a length header claiming 100 bytes
        var lengthBytes = BitConverter.GetBytes(100);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        ms.Write(lengthBytes);
        // But only write 10 bytes of payload
        ms.Write(new byte[10]);
        ms.Position = 0;

        var result = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        result.Should().BeNull();
    }

    [Test]
    public void ReceiveAsync_EmptyStream_ReturnsNull()
    {
        using var ms = new MemoryStream();
        var result = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Corrupted payload
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ReceiveAsync_CorruptedMsgPack_ThrowsOrReturnsNull()
    {
        using var ms = new MemoryStream();
        var garbage = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB };
        var lengthBytes = BitConverter.GetBytes(garbage.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);
        ms.Write(lengthBytes);
        ms.Write(garbage);
        ms.Position = 0;

        // MessagePack should throw on corrupted data
        var act = () => IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        act.Should().Throw<Exception>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Multiple sequential messages
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void MultipleMessages_Sequential_RoundTrip()
    {
        using var ms = new MemoryStream();

        var msg1 = new IpcMessage { Type = MessageType.Heartbeat, RequestId = "1" };
        var msg2 = new IpcMessage { Type = MessageType.Shutdown, RequestId = "2" };
        var msg3 = new IpcMessage { Type = MessageType.Error, RequestId = "3" };

        IpcTransport.SendAsync(ms, msg1).GetAwaiter().GetResult();
        IpcTransport.SendAsync(ms, msg2).GetAwaiter().GetResult();
        IpcTransport.SendAsync(ms, msg3).GetAwaiter().GetResult();

        ms.Position = 0;

        var r1 = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        var r2 = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        var r3 = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();

        r1!.RequestId.Should().Be("1");
        r1.Type.Should().Be(MessageType.Heartbeat);
        r2!.RequestId.Should().Be("2");
        r2.Type.Should().Be(MessageType.Shutdown);
        r3!.RequestId.Should().Be("3");
        r3.Type.Should().Be(MessageType.Error);
    }

    [Test]
    public void ReceiveAsync_AfterAllMessagesRead_ReturnsNull()
    {
        using var ms = new MemoryStream();
        var msg = new IpcMessage { Type = MessageType.Heartbeat, RequestId = "only" };
        IpcTransport.SendAsync(ms, msg).GetAwaiter().GetResult();
        ms.Position = 0;

        var r1 = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        r1.Should().NotBeNull();

        var r2 = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();
        r2.Should().BeNull("stream exhausted");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Timeout behavior
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ReceiveAsync_WithCustomTimeout_TimesOutOnSlowStream()
    {
        // Use a stream that never produces data
        var slowStream = new NeverEndingStream();
        var result = IpcTransport.ReceiveAsync(slowStream, timeout: TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult();
        result.Should().BeNull("should timeout on a stream that never sends data");
    }

    [Test]
    public void ReceiveAsync_WithCancellation_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var slowStream = new NeverEndingStream();
        var act = () => IpcTransport.ReceiveAsync(slowStream, cts.Token).GetAwaiter().GetResult();
        act.Should().Throw<OperationCanceledException>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Big-endian length prefix verification
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void SendAsync_WritesLengthInBigEndian()
    {
        using var ms = new MemoryStream();
        var msg = new IpcMessage { Type = MessageType.Heartbeat, RequestId = "be" };
        IpcTransport.SendAsync(ms, msg).GetAwaiter().GetResult();

        ms.Position = 0;
        var firstFour = new byte[4];
        ms.Read(firstFour, 0, 4);

        // Big-endian: MSB first. For a small message, first bytes should be 0x00
        firstFour[0].Should().Be(0x00, "big-endian MSB for small message");
        firstFour[1].Should().Be(0x00, "big-endian for small message");
    }

    /// <summary>
    /// A stream that blocks indefinitely on read (for timeout testing).
    /// </summary>
    private sealed class NeverEndingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // Block until cancellation
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

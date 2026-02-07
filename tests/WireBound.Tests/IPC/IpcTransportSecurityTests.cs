using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

public class IpcTransportSecurityTests
{
    [Test]
    public async Task ReceiveAsync_OversizedLength_ReturnsNull()
    {
        using var stream = new MemoryStream();

        // Write a length of 2MB (exceeds MaxMessageSize of 1MB)
        var oversizedLength = IpcConstants.MaxMessageSize + 1;
        var lengthBytes = BitConverter.GetBytes(oversizedLength);
        if (BitConverter.IsLittleEndian) Array.Reverse(lengthBytes);
        await stream.WriteAsync(lengthBytes);

        // Write some dummy payload bytes
        var dummy = new byte[100];
        await stream.WriteAsync(dummy);

        stream.Position = 0;
        var result = await IpcTransport.ReceiveAsync(stream);
        result.Should().BeNull("oversized messages must be rejected");
    }

    [Test]
    public async Task ReceiveAsync_NegativeLength_ReturnsNull()
    {
        using var stream = new MemoryStream();

        // Write -1 as the length (big-endian)
        var negativeLength = BitConverter.GetBytes(-1);
        if (BitConverter.IsLittleEndian) Array.Reverse(negativeLength);
        await stream.WriteAsync(negativeLength);

        stream.Position = 0;
        var result = await IpcTransport.ReceiveAsync(stream);
        result.Should().BeNull("negative lengths must be rejected");
    }

    [Test]
    public async Task ReceiveAsync_ZeroLength_ReturnsNull()
    {
        using var stream = new MemoryStream();

        var zeroLength = BitConverter.GetBytes(0);
        if (BitConverter.IsLittleEndian) Array.Reverse(zeroLength);
        await stream.WriteAsync(zeroLength);

        stream.Position = 0;
        var result = await IpcTransport.ReceiveAsync(stream);
        result.Should().BeNull("zero-length messages must be rejected");
    }

    [Test]
    public async Task ReceiveAsync_Timeout_ReturnsNull()
    {
        // A stream that blocks forever (never returns data)
        using var stream = new BlockingStream();
        var result = await IpcTransport.ReceiveAsync(stream, timeout: TimeSpan.FromMilliseconds(100));
        result.Should().BeNull("should return null on timeout, not hang");
    }

    [Test]
    public async Task ReceiveAsync_SlowStream_ReassemblesCorrectly()
    {
        // Create a valid message
        using var normalStream = new MemoryStream();
        var original = new IpcMessage
        {
            Type = MessageType.Heartbeat,
            RequestId = "slow-test",
            Payload = IpcTransport.SerializePayload(new HeartbeatRequest { SessionId = "s1" })
        };
        await IpcTransport.SendAsync(normalStream, original);

        // Wrap in a stream that returns 1 byte at a time
        normalStream.Position = 0;
        using var slowStream = new SlowStream(normalStream.ToArray());

        var received = await IpcTransport.ReceiveAsync(slowStream, timeout: TimeSpan.FromSeconds(5));
        received.Should().NotBeNull("should reassemble from slow stream");
        received!.RequestId.Should().Be("slow-test");
    }

    /// <summary>A stream that always blocks on ReadAsync (simulates hung connection).</summary>
    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            // Block until cancellation
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
    }

    /// <summary>A stream that returns 1 byte per read call (simulates slow network).</summary>
    private sealed class SlowStream(byte[] data) : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => _position; set => _position = (int)value; }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_position >= data.Length) return ValueTask.FromResult(0);
            buffer.Span[0] = data[_position++];
            return ValueTask.FromResult(1);
        }
    }
}

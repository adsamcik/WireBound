using System.Buffers.Binary;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

public class IpcTransportFramingTests
{
    [Test]
    public async Task SendAsync_RoundTrip_ReceiveAsync_PreservesPayload()
    {
        using var stream = new MemoryStream();
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        var original = new IpcMessage
        {
            Type = MessageType.Heartbeat,
            RequestId = "round-trip",
            Payload = payload
        };

        await IpcTransport.SendAsync(stream, original);

        stream.Position = 0;
        var received = await IpcTransport.ReceiveAsync(stream);

        received.Type.Should().Be(MessageType.Heartbeat);
        received.RequestId.Should().Be("round-trip");
        received.Payload.Should().Equal(payload);
    }

    [Test]
    public async Task SendAsync_BigEndianLengthPrefix()
    {
        using var stream = new MemoryStream();
        var message = new IpcMessage
        {
            Type = MessageType.Heartbeat,
            RequestId = "big-endian",
            Payload = [0xCA, 0xFE]
        };

        await IpcTransport.SendAsync(stream, message);

        var bytes = stream.ToArray();
        var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(0, sizeof(int)));
        length.Should().Be(bytes.Length - sizeof(int));
    }

    [Test]
    public async Task ReceiveAsync_PartialLengthPrefix_ThrowsIpcFramingException()
    {
        using var stream = new MemoryStream([0x00, 0x00]);

        Func<Task> act = async () => await IpcTransport.ReceiveAsync(stream);

        await act.Should().ThrowAsync<IpcFramingException>();
    }

    [Test]
    public async Task ReceiveAsync_NegativeLength_ThrowsIpcFramingException()
    {
        using var stream = CreateRawFrame(length: -1, payload: []);

        Func<Task> act = async () => await IpcTransport.ReceiveAsync(stream);

        var exception = await act.Should().ThrowAsync<IpcFramingException>();
        exception.Which.Message.Should().Contain("-1");
    }

    [Test]
    public async Task ReceiveAsync_OversizedLength_ThrowsIpcFramingException()
    {
        using var stream = CreateRawFrame(IpcConstants.MaxMessageSize + 1, []);

        Func<Task> act = async () => await IpcTransport.ReceiveAsync(stream);

        var exception = await act.Should().ThrowAsync<IpcFramingException>();
        exception.Which.Message.Should().Contain((IpcConstants.MaxMessageSize + 1).ToString());
    }

    [Test]
    public async Task ReceiveAsync_TruncatedPayload_ThrowsIpcFramingException()
    {
        using var stream = CreateRawFrame(length: 8, payload: [1, 2, 3]);

        Func<Task> act = async () => await IpcTransport.ReceiveAsync(stream);

        await act.Should().ThrowAsync<IpcFramingException>();
    }

    [Test]
    public async Task ReceiveAsync_GarbagePayload_ThrowsIpcFramingException()
    {
        using var stream = CreateRawFrame(length: 1, payload: [0xC1]);

        Func<Task> act = async () => await IpcTransport.ReceiveAsync(stream);

        var exception = await act.Should().ThrowAsync<IpcFramingException>();
        exception.Which.InnerException.Should().NotBeNull();
    }

    [Test]
    public async Task ReceiveAsync_CallerCancellation_ThrowsOperationCanceledException()
    {
        using var stream = new CancellationAwareBlockingStream();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await IpcTransport.ReceiveAsync(stream, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task SendAsync_ConcurrentSenders_FramesNeverInterleave()
    {
        const int messageCount = 50;
        using var stream = new PerWriteLockedMemoryStream();
        var expectedRequestIds = Enumerable.Range(0, messageCount)
            .Select(i => $"req-{i}")
            .ToHashSet(StringComparer.Ordinal);

        var tasks = Enumerable.Range(0, messageCount)
            .Select(i => IpcTransport.SendAsync(
                stream,
                new IpcMessage
                {
                    Type = MessageType.Heartbeat,
                    RequestId = $"req-{i}",
                    Payload = IpcTransport.SerializePayload(new HeartbeatRequest { SessionId = $"session-{i}" })
                }))
            .ToArray();

        await Task.WhenAll(tasks);

        stream.Position = 0;
        var receivedRequestIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < messageCount; i++)
        {
            var message = await IpcTransport.ReceiveAsync(stream, timeout: TimeSpan.Zero);
            receivedRequestIds.Add(message.RequestId).Should().BeTrue("each frame should deserialize exactly once");

            var heartbeat = IpcTransport.DeserializePayload<HeartbeatRequest>(message.Payload);
            heartbeat.SessionId.Should().StartWith("session-");
        }

        receivedRequestIds.Should().BeEquivalentTo(expectedRequestIds);
    }

    private static MemoryStream CreateRawFrame(int length, byte[] payload)
    {
        var frame = new byte[sizeof(int) + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, sizeof(int)), length);
        payload.CopyTo(frame.AsSpan(sizeof(int)));
        return new MemoryStream(frame);
    }

    private sealed class CancellationAwareBlockingStream : Stream
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

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<int>(Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ContinueWith(_ => 0));
        }
    }

    private sealed class PerWriteLockedMemoryStream : Stream
    {
        private readonly MemoryStream _inner = new();
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await Task.Yield();
                _inner.Write(buffer.Span);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _writeLock.Dispose();
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

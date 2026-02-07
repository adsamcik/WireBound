using WireBound.IPC.Messages;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

public class IpcTransportTests
{
    [Test]
    public async Task SendAndReceive_RoundTrip_PreservesMessage()
    {
        using var stream = new MemoryStream();

        var original = new IpcMessage
        {
            Type = MessageType.Heartbeat,
            RequestId = "test-123",
            Payload = IpcTransport.SerializePayload(new HeartbeatRequest { SessionId = "session-abc" })
        };

        await IpcTransport.SendAsync(stream, original);

        stream.Position = 0;
        var received = await IpcTransport.ReceiveAsync(stream);

        received.Should().NotBeNull();
        received!.Type.Should().Be(MessageType.Heartbeat);
        received.RequestId.Should().Be("test-123");

        var payload = IpcTransport.DeserializePayload<HeartbeatRequest>(received.Payload);
        payload.SessionId.Should().Be("session-abc");
    }

    [Test]
    public async Task SendAndReceive_AuthenticateMessage_PreservesAllFields()
    {
        using var stream = new MemoryStream();

        var authRequest = new AuthenticateRequest
        {
            ClientPid = 42,
            Timestamp = 1234567890,
            Signature = "test-signature",
            ExecutablePath = @"C:\app\wirebound.exe"
        };

        var message = new IpcMessage
        {
            Type = MessageType.Authenticate,
            Payload = IpcTransport.SerializePayload(authRequest)
        };

        await IpcTransport.SendAsync(stream, message);
        stream.Position = 0;

        var received = await IpcTransport.ReceiveAsync(stream);
        var payload = IpcTransport.DeserializePayload<AuthenticateRequest>(received!.Payload);

        payload.ClientPid.Should().Be(42);
        payload.Timestamp.Should().Be(1234567890);
        payload.Signature.Should().Be("test-signature");
        payload.ExecutablePath.Should().Be(@"C:\app\wirebound.exe");
    }

    [Test]
    public async Task SendAndReceive_ConnectionStatsResponse_PreservesNestedData()
    {
        using var stream = new MemoryStream();

        var stats = new ConnectionStatsResponse
        {
            Success = true,
            Processes =
            [
                new ProcessConnectionStats
                {
                    ProcessId = 100,
                    ProcessName = "chrome",
                    BytesSent = 1024,
                    BytesReceived = 2048,
                    Connections =
                    [
                        new ConnectionByteStats
                        {
                            LocalAddress = "192.168.1.1",
                            LocalPort = 54321,
                            RemoteAddress = "93.184.216.34",
                            RemotePort = 443,
                            Protocol = 6,
                            BytesSent = 512,
                            BytesReceived = 1024
                        }
                    ]
                }
            ]
        };

        var message = new IpcMessage
        {
            Type = MessageType.ConnectionStats,
            Payload = IpcTransport.SerializePayload(stats)
        };

        await IpcTransport.SendAsync(stream, message);
        stream.Position = 0;

        var received = await IpcTransport.ReceiveAsync(stream);
        var payload = IpcTransport.DeserializePayload<ConnectionStatsResponse>(received!.Payload);

        payload.Success.Should().BeTrue();
        payload.Processes.Should().HaveCount(1);
        payload.Processes[0].ProcessName.Should().Be("chrome");
        payload.Processes[0].Connections.Should().HaveCount(1);
        payload.Processes[0].Connections[0].RemotePort.Should().Be(443);
        payload.Processes[0].Connections[0].BytesReceived.Should().Be(1024);
    }

    [Test]
    public async Task SendAndReceive_MultipleMessages_AllPreserved()
    {
        using var stream = new MemoryStream();

        for (var i = 0; i < 5; i++)
        {
            var msg = new IpcMessage
            {
                Type = MessageType.Heartbeat,
                RequestId = $"req-{i}",
                Payload = IpcTransport.SerializePayload(new HeartbeatRequest { SessionId = $"session-{i}" })
            };
            await IpcTransport.SendAsync(stream, msg);
        }

        stream.Position = 0;

        for (var i = 0; i < 5; i++)
        {
            var received = await IpcTransport.ReceiveAsync(stream);
            received.Should().NotBeNull();
            received!.RequestId.Should().Be($"req-{i}");

            var payload = IpcTransport.DeserializePayload<HeartbeatRequest>(received.Payload);
            payload.SessionId.Should().Be($"session-{i}");
        }
    }

    [Test]
    public async Task ReceiveAsync_EmptyStream_ReturnsNull()
    {
        using var stream = new MemoryStream();
        var result = await IpcTransport.ReceiveAsync(stream);
        result.Should().BeNull();
    }

    [Test]
    public async Task ReceiveAsync_TruncatedLength_ReturnsNull()
    {
        using var stream = new MemoryStream([0x01, 0x02]); // Only 2 bytes, need 4
        var result = await IpcTransport.ReceiveAsync(stream);
        result.Should().BeNull();
    }

    [Test]
    public async Task SerializePayload_DeserializePayload_Roundtrip()
    {
        var original = new ErrorResponse { Error = "test error", Details = "some details" };
        var bytes = IpcTransport.SerializePayload(original);
        var deserialized = IpcTransport.DeserializePayload<ErrorResponse>(bytes);

        deserialized.Error.Should().Be("test error");
        deserialized.Details.Should().Be("some details");
    }
}

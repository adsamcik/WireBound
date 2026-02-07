using WireBound.IPC.Messages;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

/// <summary>
/// Tests MessagePack round-trip serialization for ALL IPC message DTOs.
/// Ensures key ordering is stable and all field types serialize correctly.
/// </summary>
public class MessageSerializationTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // IpcMessage envelope
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void IpcMessage_RoundTrip_AllFields()
    {
        var original = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "abc123",
            Payload = [0x01, 0x02, 0x03]
        };

        var bytes = IpcTransport.SerializePayload(original);
        var deserialized = IpcTransport.DeserializePayload<IpcMessage>(bytes);

        deserialized.Type.Should().Be(MessageType.Authenticate);
        deserialized.RequestId.Should().Be("abc123");
        deserialized.Payload.Should().BeEquivalentTo(new byte[] { 0x01, 0x02, 0x03 });
    }

    [Test]
    public void IpcMessage_DefaultValues()
    {
        var msg = new IpcMessage();
        var bytes = IpcTransport.SerializePayload(msg);
        var deserialized = IpcTransport.DeserializePayload<IpcMessage>(bytes);

        deserialized.Type.Should().Be((MessageType)0, "default enum value is 0");
        deserialized.RequestId.Should().NotBeNullOrEmpty();
        deserialized.Payload.Should().BeEmpty();
    }

    [Test]
    [Arguments(MessageType.Authenticate)]
    [Arguments(MessageType.ConnectionStats)]
    [Arguments(MessageType.ProcessStats)]
    [Arguments(MessageType.Heartbeat)]
    [Arguments(MessageType.Shutdown)]
    [Arguments(MessageType.Error)]
    public void IpcMessage_AllMessageTypes_Serialize(MessageType type)
    {
        var msg = new IpcMessage { Type = type, RequestId = "test" };
        var bytes = IpcTransport.SerializePayload(msg);
        var deserialized = IpcTransport.DeserializePayload<IpcMessage>(bytes);
        deserialized.Type.Should().Be(type);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // AuthenticateRequest / AuthenticateResponse
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void AuthenticateRequest_RoundTrip()
    {
        var original = new AuthenticateRequest
        {
            ClientPid = 12345,
            Timestamp = 1700000000L,
            Signature = "base64sig==",
            ExecutablePath = @"C:\App\wirebound.exe"
        };

        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<AuthenticateRequest>(bytes);

        d.ClientPid.Should().Be(12345);
        d.Timestamp.Should().Be(1700000000L);
        d.Signature.Should().Be("base64sig==");
        d.ExecutablePath.Should().Be(@"C:\App\wirebound.exe");
    }

    [Test]
    public void AuthenticateRequest_EmptyStrings()
    {
        var original = new AuthenticateRequest { ClientPid = 0, Timestamp = 0 };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<AuthenticateRequest>(bytes);

        d.ClientPid.Should().Be(0);
        d.Timestamp.Should().Be(0);
        d.Signature.Should().BeEmpty();
        d.ExecutablePath.Should().BeEmpty();
    }

    [Test]
    public void AuthenticateResponse_Success_RoundTrip()
    {
        var original = new AuthenticateResponse
        {
            Success = true,
            SessionId = "session123",
            ExpiresAtUtc = 1700028800L
        };

        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<AuthenticateResponse>(bytes);

        d.Success.Should().BeTrue();
        d.SessionId.Should().Be("session123");
        d.ExpiresAtUtc.Should().Be(1700028800L);
        d.ErrorMessage.Should().BeNull();
    }

    [Test]
    public void AuthenticateResponse_Failure_RoundTrip()
    {
        var original = new AuthenticateResponse
        {
            Success = false,
            ErrorMessage = "Invalid HMAC"
        };

        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<AuthenticateResponse>(bytes);

        d.Success.Should().BeFalse();
        d.ErrorMessage.Should().Be("Invalid HMAC");
        d.SessionId.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ConnectionStatsRequest / ConnectionStatsResponse
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ConnectionStatsRequest_RoundTrip()
    {
        var original = new ConnectionStatsRequest { SessionId = "sess1" };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ConnectionStatsRequest>(bytes);
        d.SessionId.Should().Be("sess1");
    }

    [Test]
    public void ConnectionStatsResponse_WithProcesses_RoundTrip()
    {
        var original = new ConnectionStatsResponse
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
                            RemoteAddress = "8.8.8.8",
                            RemotePort = 443,
                            Protocol = 6,
                            BytesSent = 512,
                            BytesReceived = 1024
                        }
                    ]
                }
            ]
        };

        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ConnectionStatsResponse>(bytes);

        d.Success.Should().BeTrue();
        d.Processes.Should().HaveCount(1);
        d.Processes[0].ProcessId.Should().Be(100);
        d.Processes[0].ProcessName.Should().Be("chrome");
        d.Processes[0].BytesSent.Should().Be(1024);
        d.Processes[0].BytesReceived.Should().Be(2048);
        d.Processes[0].Connections.Should().HaveCount(1);

        var conn = d.Processes[0].Connections[0];
        conn.LocalAddress.Should().Be("192.168.1.1");
        conn.LocalPort.Should().Be(54321);
        conn.RemoteAddress.Should().Be("8.8.8.8");
        conn.RemotePort.Should().Be(443);
        conn.Protocol.Should().Be(6);
        conn.BytesSent.Should().Be(512);
        conn.BytesReceived.Should().Be(1024);
    }

    [Test]
    public void ConnectionStatsResponse_Empty_RoundTrip()
    {
        var original = new ConnectionStatsResponse { Success = true };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ConnectionStatsResponse>(bytes);

        d.Success.Should().BeTrue();
        d.Processes.Should().BeEmpty();
        d.ErrorMessage.Should().BeNull();
    }

    [Test]
    public void ConnectionStatsResponse_Error_RoundTrip()
    {
        var original = new ConnectionStatsResponse { Success = false, ErrorMessage = "Timeout" };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ConnectionStatsResponse>(bytes);

        d.Success.Should().BeFalse();
        d.ErrorMessage.Should().Be("Timeout");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ProcessStatsRequest / ProcessStatsResponse
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ProcessStatsRequest_RoundTrip()
    {
        var original = new ProcessStatsRequest
        {
            SessionId = "sess1",
            ProcessIds = [100, 200, 300]
        };

        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ProcessStatsRequest>(bytes);

        d.SessionId.Should().Be("sess1");
        d.ProcessIds.Should().BeEquivalentTo(new[] { 100, 200, 300 });
    }

    [Test]
    public void ProcessStatsRequest_EmptyPids()
    {
        var original = new ProcessStatsRequest { SessionId = "s" };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ProcessStatsRequest>(bytes);
        d.ProcessIds.Should().BeEmpty();
    }

    [Test]
    public void ProcessStatsResponse_WithProcesses_RoundTrip()
    {
        var original = new ProcessStatsResponse
        {
            Success = true,
            Processes =
            [
                new ProcessByteStats
                {
                    ProcessId = 42,
                    ProcessName = "firefox",
                    TotalBytesSent = 999999L,
                    TotalBytesReceived = 888888L,
                    ActiveConnectionCount = 15
                }
            ]
        };

        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ProcessStatsResponse>(bytes);

        d.Success.Should().BeTrue();
        d.Processes.Should().HaveCount(1);
        d.Processes[0].ProcessId.Should().Be(42);
        d.Processes[0].ProcessName.Should().Be("firefox");
        d.Processes[0].TotalBytesSent.Should().Be(999999L);
        d.Processes[0].TotalBytesReceived.Should().Be(888888L);
        d.Processes[0].ActiveConnectionCount.Should().Be(15);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HeartbeatRequest / HeartbeatResponse
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HeartbeatRequest_RoundTrip()
    {
        var original = new HeartbeatRequest { SessionId = "hb-session" };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<HeartbeatRequest>(bytes);
        d.SessionId.Should().Be("hb-session");
    }

    [Test]
    public void HeartbeatResponse_RoundTrip()
    {
        var original = new HeartbeatResponse
        {
            Alive = true,
            UptimeSeconds = 3600,
            ActiveSessions = 3
        };

        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<HeartbeatResponse>(bytes);

        d.Alive.Should().BeTrue();
        d.UptimeSeconds.Should().Be(3600);
        d.ActiveSessions.Should().Be(3);
    }

    [Test]
    public void HeartbeatResponse_NotAlive()
    {
        var original = new HeartbeatResponse { Alive = false };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<HeartbeatResponse>(bytes);
        d.Alive.Should().BeFalse();
        d.UptimeSeconds.Should().Be(0);
        d.ActiveSessions.Should().Be(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ShutdownRequest / ErrorResponse
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ShutdownRequest_RoundTrip()
    {
        var original = new ShutdownRequest { SessionId = "shutdown-sess", Reason = "User requested" };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ShutdownRequest>(bytes);

        d.SessionId.Should().Be("shutdown-sess");
        d.Reason.Should().Be("User requested");
    }

    [Test]
    public void ShutdownRequest_EmptyReason()
    {
        var original = new ShutdownRequest { SessionId = "s", Reason = "" };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ShutdownRequest>(bytes);
        d.Reason.Should().BeEmpty();
    }

    [Test]
    public void ShutdownRequest_DefaultReason()
    {
        var original = new ShutdownRequest { SessionId = "s" };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ShutdownRequest>(bytes);
        d.Reason.Should().Be("Client requested shutdown");
    }

    [Test]
    public void ErrorResponse_RoundTrip()
    {
        var original = new ErrorResponse { Error = "Something failed", Details = "Stack trace..." };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ErrorResponse>(bytes);

        d.Error.Should().Be("Something failed");
        d.Details.Should().Be("Stack trace...");
    }

    [Test]
    public void ErrorResponse_NullDetails()
    {
        var original = new ErrorResponse { Error = "Oops" };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ErrorResponse>(bytes);

        d.Error.Should().Be("Oops");
        d.Details.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Nested payload embedding (as used in real IPC)
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void NestedPayload_AuthRequest_InIpcMessage()
    {
        var authReq = new AuthenticateRequest
        {
            ClientPid = 5678,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Signature = "sig",
            ExecutablePath = "/usr/bin/wirebound"
        };

        var msg = new IpcMessage
        {
            Type = MessageType.Authenticate,
            Payload = IpcTransport.SerializePayload(authReq)
        };

        // Simulate send/receive via stream
        using var ms = new MemoryStream();
        IpcTransport.SendAsync(ms, msg).GetAwaiter().GetResult();

        ms.Position = 0;
        var received = IpcTransport.ReceiveAsync(ms).GetAwaiter().GetResult();

        received.Should().NotBeNull();
        received!.Type.Should().Be(MessageType.Authenticate);

        var innerReq = IpcTransport.DeserializePayload<AuthenticateRequest>(received.Payload);
        innerReq.ClientPid.Should().Be(5678);
        innerReq.ExecutablePath.Should().Be("/usr/bin/wirebound");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Large / boundary payloads
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void LargePayload_ManyProcesses_RoundTrip()
    {
        var processes = Enumerable.Range(0, 100).Select(i => new ProcessByteStats
        {
            ProcessId = i,
            ProcessName = $"proc-{i}",
            TotalBytesSent = i * 1000L,
            TotalBytesReceived = i * 2000L,
            ActiveConnectionCount = i % 10
        }).ToList();

        var original = new ProcessStatsResponse { Success = true, Processes = processes };
        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<ProcessStatsResponse>(bytes);

        d.Processes.Should().HaveCount(100);
        d.Processes[99].ProcessId.Should().Be(99);
        d.Processes[99].TotalBytesSent.Should().Be(99000L);
    }

    [Test]
    public void LongStringFields_RoundTrip()
    {
        var longPath = new string('A', 10_000);
        var original = new AuthenticateRequest
        {
            ClientPid = 1,
            Timestamp = 1,
            Signature = new string('B', 1000),
            ExecutablePath = longPath
        };

        var bytes = IpcTransport.SerializePayload(original);
        var d = IpcTransport.DeserializePayload<AuthenticateRequest>(bytes);
        d.ExecutablePath.Should().HaveLength(10_000);
    }
}

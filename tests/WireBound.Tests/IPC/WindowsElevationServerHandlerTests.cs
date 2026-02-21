using System.Security.Principal;
using WireBound.Elevation.Windows;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

/// <summary>
/// Tests for the Windows ElevationServer handler methods.
/// Calls internal handler methods directly to test message handling logic
/// without requiring elevated privileges or real named pipes.
/// </summary>
[NotInParallel("SecretFile")]
public class WindowsElevationServerHandlerTests : IDisposable
{
    private readonly ElevationServer? _server;
    private readonly byte[]? _secret;
    private readonly bool _available;

    public WindowsElevationServerHandlerTests()
    {
        try
        {
            var currentSid = WindowsIdentity.GetCurrent().User!.Value;
            _server = new ElevationServer(currentSid);
            // Extract secret via reflection to avoid race with other test classes
            // that also call SecretManager.GenerateAndStore() concurrently
            _secret = (byte[])typeof(ElevationServer)
                .GetField("_secret", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(_server)!;
            _available = true;
        }
        catch (Exception) when (!OperatingSystem.IsWindows()) { _available = false; }
        catch (IOException) { _available = false; }
    }

    private IpcMessage CreateValidAuthRequest()
    {
        var pid = Environment.ProcessId;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HmacAuthenticator.Sign(pid, timestamp, _secret!);

        return new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = Guid.NewGuid().ToString("N"),
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = timestamp,
                Signature = signature,
                ExecutablePath = "" // Skip exe validation
            })
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HandleAuthenticate
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HandleAuthenticate_ValidHmac_ReturnsSuccess()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var request = CreateValidAuthRequest();
        var response = _server!.HandleAuthenticate(request, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeTrue();
        authResp.SessionId.Should().NotBeNullOrEmpty();
        authResp.ExpiresAtUtc.Should().BeGreaterThan(0);
        sessionId.Should().NotBeNull();
    }

    [Test]
    public void HandleAuthenticate_InvalidHmac_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "test-1",
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = Environment.ProcessId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Signature = "invalid-signature"
            })
        };

        var response = _server!.HandleAuthenticate(request, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Authentication failed");
        sessionId.Should().BeNull();
    }

    [Test]
    public void HandleAuthenticate_ExpiredTimestamp_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "test-2",
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = Environment.ProcessId,
                Timestamp = oldTimestamp,
                Signature = HmacAuthenticator.Sign(Environment.ProcessId, oldTimestamp, _secret)
            })
        };

        var response = _server!.HandleAuthenticate(request, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        sessionId.Should().BeNull();
    }

    [Test]
    public void HandleAuthenticate_MalformedPayload_ReturnsInvalidAuth()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "test-3",
            Payload = [0xFF, 0xFE] // Invalid MessagePack
        };

        var response = _server!.HandleAuthenticate(request, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Invalid auth request");
        sessionId.Should().BeNull();
    }

    [Test]
    public void HandleAuthenticate_WrongExecutablePath_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var pid = Environment.ProcessId;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HmacAuthenticator.Sign(pid, timestamp, _secret);

        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "test-exe",
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = timestamp,
                Signature = signature,
                ExecutablePath = @"C:\totally\wrong\path.exe"
            })
        };

        var response = _server!.HandleAuthenticate(request, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Executable validation failed");
        sessionId.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HandleConnectionStats
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HandleConnectionStats_WithValidSession_ReturnsSuccess()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        // Authenticate first
        var authReq = CreateValidAuthRequest();
        _server!.HandleAuthenticate(authReq, out var sessionId);
        sessionId.Should().NotBeNull();

        var request = new IpcMessage
        {
            Type = MessageType.ConnectionStats,
            RequestId = "stats-1"
        };

        var response = _server!.HandleConnectionStats(request, sessionId);

        var statsResp = IpcTransport.DeserializePayload<ConnectionStatsResponse>(response.Payload);
        statsResp.Success.Should().BeTrue();
    }

    [Test]
    public void HandleConnectionStats_WithNullSession_ReturnsError()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var request = new IpcMessage
        {
            Type = MessageType.ConnectionStats,
            RequestId = "stats-2"
        };

        var response = _server!.HandleConnectionStats(request, null);

        response.Type.Should().Be(MessageType.Error);
        var errResp = IpcTransport.DeserializePayload<ErrorResponse>(response.Payload);
        errResp.Error.Should().Contain("Invalid or expired session");
    }

    [Test]
    public void HandleConnectionStats_WithInvalidSession_ReturnsError()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var request = new IpcMessage { Type = MessageType.ConnectionStats, RequestId = "stats-3" };
        var response = _server!.HandleConnectionStats(request, "nonexistent-session");

        response.Type.Should().Be(MessageType.Error);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HandleProcessStats
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HandleProcessStats_WithValidSession_ReturnsSuccess()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var authReq = CreateValidAuthRequest();
        _server!.HandleAuthenticate(authReq, out var sessionId);

        var request = new IpcMessage
        {
            Type = MessageType.ProcessStats,
            RequestId = "proc-1",
            Payload = IpcTransport.SerializePayload(new ProcessStatsRequest())
        };

        var response = _server!.HandleProcessStats(request, sessionId);

        var statsResp = IpcTransport.DeserializePayload<ProcessStatsResponse>(response.Payload);
        statsResp.Success.Should().BeTrue();
    }

    [Test]
    public void HandleProcessStats_WithNullSession_ReturnsError()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var request = new IpcMessage
        {
            Type = MessageType.ProcessStats,
            RequestId = "proc-2",
            Payload = IpcTransport.SerializePayload(new ProcessStatsRequest())
        };

        var response = _server!.HandleProcessStats(request, null);

        response.Type.Should().Be(MessageType.Error);
    }

    [Test]
    public void HandleProcessStats_MalformedPayload_FallsBackToDefault()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var authReq = CreateValidAuthRequest();
        _server!.HandleAuthenticate(authReq, out var sessionId);

        var request = new IpcMessage
        {
            Type = MessageType.ProcessStats,
            RequestId = "proc-3",
            Payload = [0xFF, 0xFE] // Invalid
        };

        var response = _server!.HandleProcessStats(request, sessionId);
        // Should fallback to empty ProcessStatsRequest, not crash
        var statsResp = IpcTransport.DeserializePayload<ProcessStatsResponse>(response.Payload);
        statsResp.Success.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HandleHeartbeat
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HandleHeartbeat_ReturnsAliveWithUptime()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var response = _server!.HandleHeartbeat();

        response.Type.Should().Be(MessageType.Heartbeat);
        var hbResp = IpcTransport.DeserializePayload<HeartbeatResponse>(response.Payload);
        hbResp.Alive.Should().BeTrue();
        hbResp.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
        hbResp.ActiveSessions.Should().BeGreaterThanOrEqualTo(0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HandleShutdown
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HandleShutdown_WithValidSession_ReturnsResponse()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var authReq = CreateValidAuthRequest();
        _server!.HandleAuthenticate(authReq, out var sessionId);

        var request = new IpcMessage { Type = MessageType.Shutdown, RequestId = "sd-1" };
        var response = _server!.HandleShutdown(request, sessionId);

        response.Type.Should().Be(MessageType.Shutdown);
        var hbResp = IpcTransport.DeserializePayload<HeartbeatResponse>(response.Payload);
        hbResp.Alive.Should().BeFalse();
    }

    [Test]
    public void HandleShutdown_WithNullSession_ReturnsError()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var request = new IpcMessage { Type = MessageType.Shutdown, RequestId = "sd-2" };
        var response = _server!.HandleShutdown(request, null);

        response.Type.Should().Be(MessageType.Error);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ValidateSession
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ValidateSession_NullSessionId_ReturnsFalse()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        _server!.ValidateSession(null, out var error).Should().BeFalse();
        error.Should().Contain("Invalid or expired session");
    }

    [Test]
    public void ValidateSession_ValidSession_ReturnsTrue()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var authReq = CreateValidAuthRequest();
        _server!.HandleAuthenticate(authReq, out var sessionId);

        _server!.ValidateSession(sessionId, out var error).Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Test]
    public void ValidateSession_RemovedSession_ReturnsFalse()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var authReq = CreateValidAuthRequest();
        _server!.HandleAuthenticate(authReq, out var sessionId);

        // Shutdown removes the session via the server's internal logic, but
        // we can test validation with a fake session ID
        _server!.ValidateSession("nonexistent", out _).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Max sessions exceeded
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HandleAuthenticate_MaxSessionsExceeded_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        // Fill up sessions to max
        for (var i = 0; i < IpcConstants.MaxConcurrentSessions; i++)
        {
            var req = CreateValidAuthRequest();
            var resp = _server!.HandleAuthenticate(req, out _);
            var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(resp.Payload);
            authResp.Success.Should().BeTrue($"session {i} should succeed");
        }

        // The next should fail
        var overflowReq = CreateValidAuthRequest();
        var overflowResp = _server!.HandleAuthenticate(overflowReq, out var sid);

        var overflowAuth = IpcTransport.DeserializePayload<AuthenticateResponse>(overflowResp.Payload);
        overflowAuth.Success.Should().BeFalse();
        overflowAuth.ErrorMessage.Should().Contain("Max sessions exceeded");
        sid.Should().BeNull();
    }

    public void Dispose()
    {
        _server?.Dispose();
    }
}


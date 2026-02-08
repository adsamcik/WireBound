#pragma warning disable CA1416

using WireBound.Elevation.Linux;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

/// <summary>
/// Tests for the Linux ElevationServer handler methods.
/// Calls internal handler methods directly to test message handling logic
/// without requiring elevated privileges or real Unix domain sockets.
/// </summary>
[NotInParallel("SecretFile")]
public class LinuxElevationServerHandlerTests : IDisposable
{
    private readonly ElevationServer? _server;
    private readonly byte[]? _secret;
    private readonly bool _available;

    public LinuxElevationServerHandlerTests()
    {
        try
        {
            _server = new ElevationServer();
            // Extract secret via reflection to avoid race with other test classes
            // that also call SecretManager.GenerateAndStore() concurrently
            _secret = (byte[])typeof(ElevationServer)
                .GetField("_secret", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(_server)!;
            _available = true;
        }
        catch (IOException)
        {
            _available = false;
        }
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

    /// <summary>
    /// Authenticates and returns the session ID for tests that need a valid session.
    /// </summary>
    private string AuthenticateAndGetSession()
    {
        var authReq = CreateValidAuthRequest();
        _server!.HandleAuthenticate(authReq, Environment.ProcessId, out var sessionId);
        sessionId.Should().NotBeNull();
        return sessionId!;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HandleAuthenticate
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HandleAuthenticate_ValidHmac_ReturnsSuccess()
    {
        if (!_available) return;

        var request = CreateValidAuthRequest();
        var response = _server!.HandleAuthenticate(request, Environment.ProcessId, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeTrue();
        authResp.SessionId.Should().NotBeNullOrEmpty();
        authResp.ExpiresAtUtc.Should().BeGreaterThan(0);
        sessionId.Should().NotBeNull();
    }

    [Test]
    public void HandleAuthenticate_InvalidHmac_ReturnsFailed()
    {
        if (!_available) return;

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

        var response = _server!.HandleAuthenticate(request, Environment.ProcessId, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Authentication failed");
        sessionId.Should().BeNull();
    }

    [Test]
    public void HandleAuthenticate_ExpiredTimestamp_ReturnsFailed()
    {
        if (!_available) return;

        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds();
        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "test-2",
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = Environment.ProcessId,
                Timestamp = oldTimestamp,
                Signature = HmacAuthenticator.Sign(Environment.ProcessId, oldTimestamp, _secret!)
            })
        };

        var response = _server!.HandleAuthenticate(request, Environment.ProcessId, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        sessionId.Should().BeNull();
    }

    [Test]
    public void HandleAuthenticate_MalformedPayload_ReturnsInvalidAuth()
    {
        if (!_available) return;

        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "test-3",
            Payload = [0xFF, 0xFE] // Invalid MessagePack
        };

        var response = _server!.HandleAuthenticate(request, Environment.ProcessId, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Invalid auth request");
        sessionId.Should().BeNull();
    }

    [Test]
    public void HandleAuthenticate_WrongExecutablePath_ReturnsFailed()
    {
        if (!_available) return;

        var pid = Environment.ProcessId;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HmacAuthenticator.Sign(pid, timestamp, _secret!);

        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "test-exe",
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = timestamp,
                Signature = signature,
                ExecutablePath = "/totally/wrong/path"
            })
        };

        var response = _server!.HandleAuthenticate(request, pid, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Executable validation failed");
        sessionId.Should().BeNull();
    }

    [Test]
    public void HandleAuthenticate_PidMismatch_ReturnsFailed()
    {
        if (!_available) return;

        var request = CreateValidAuthRequest();
        // Pass a peerPid that differs from Environment.ProcessId in the auth request
        var response = _server!.HandleAuthenticate(request, Environment.ProcessId + 9999, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("PID verification failed");
        sessionId.Should().BeNull();
    }

    [Test]
    public void HandleAuthenticate_PeerPidZero_SkipsCrossVerification()
    {
        if (!_available) return;

        var request = CreateValidAuthRequest();
        // peerPid=0 means SO_PEERCRED failed, should skip cross-verification
        var response = _server!.HandleAuthenticate(request, 0, out var sessionId);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeTrue();
        authResp.SessionId.Should().NotBeNullOrEmpty();
        sessionId.Should().NotBeNull();
    }

    [Test]
    public void HandleAuthenticate_MaxSessionsExceeded_ReturnsFailed()
    {
        if (!_available) return;

        // Fill up sessions to max
        for (var i = 0; i < IpcConstants.MaxConcurrentSessions; i++)
        {
            var req = CreateValidAuthRequest();
            var resp = _server!.HandleAuthenticate(req, Environment.ProcessId, out _);
            var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(resp.Payload);
            authResp.Success.Should().BeTrue($"session {i} should succeed");
        }

        // The next should fail
        var overflowReq = CreateValidAuthRequest();
        var overflowResp = _server!.HandleAuthenticate(overflowReq, Environment.ProcessId, out var sid);

        var overflowAuth = IpcTransport.DeserializePayload<AuthenticateResponse>(overflowResp.Payload);
        overflowAuth.Success.Should().BeFalse();
        overflowAuth.ErrorMessage.Should().Contain("Max sessions exceeded");
        sid.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HandleConnectionStats
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void HandleConnectionStats_WithValidSession_ReturnsSuccess()
    {
        if (!_available) return;

        var sessionId = AuthenticateAndGetSession();

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
        if (!_available) return;

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
        if (!_available) return;

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
        if (!_available) return;

        var sessionId = AuthenticateAndGetSession();

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
        if (!_available) return;

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
        if (!_available) return;

        var sessionId = AuthenticateAndGetSession();

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
        if (!_available) return;

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
        if (!_available) return;

        var sessionId = AuthenticateAndGetSession();

        var request = new IpcMessage { Type = MessageType.Shutdown, RequestId = "sd-1" };
        var response = _server!.HandleShutdown(request, sessionId);

        response.Type.Should().Be(MessageType.Shutdown);
        var hbResp = IpcTransport.DeserializePayload<HeartbeatResponse>(response.Payload);
        hbResp.Alive.Should().BeFalse();
    }

    [Test]
    public void HandleShutdown_WithNullSession_ReturnsError()
    {
        if (!_available) return;

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
        if (!_available) return;

        _server!.ValidateSession(null, out var error).Should().BeFalse();
        error.Should().Contain("Invalid or expired session");
    }

    [Test]
    public void ValidateSession_ValidSession_ReturnsTrue()
    {
        if (!_available) return;

        var sessionId = AuthenticateAndGetSession();

        _server!.ValidateSession(sessionId, out var error).Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Test]
    public void ValidateSession_NonexistentSession_ReturnsFalse()
    {
        if (!_available) return;

        _server!.ValidateSession("nonexistent", out _).Should().BeFalse();
    }

    public void Dispose()
    {
        _server?.Dispose();
    }
}

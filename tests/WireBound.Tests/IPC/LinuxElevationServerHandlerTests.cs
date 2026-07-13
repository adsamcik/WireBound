#pragma warning disable CA1416

using System.Security.Cryptography;
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
///
/// <para>
/// The server is constructed with a <see cref="PassThroughClientIdentityVerifier"/>
/// so the tests can focus on HMAC / nonce / single-client-bind logic. The
/// production <c>LinuxClientIdentityVerifier</c> expects the caller to be
/// <c>&lt;BaseDirectory&gt;/WireBound</c>, which never matches the
/// test runner process — see the dedicated verifier tests for that path.
/// </para>
/// </summary>
[NotInParallel("SecretFile")]
public class LinuxElevationServerHandlerTests : IDisposable
{
    private readonly ElevationServer? _server;
    private readonly byte[]? _secret;
    private readonly bool _available;

    public LinuxElevationServerHandlerTests()
    {
        if (!OperatingSystem.IsLinux())
        {
            _available = false;
            return;
        }

        try
        {
            _server = new ElevationServer(new PassThroughClientIdentityVerifier());
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

    /// <summary>
    /// Builds a fully-valid auth request and returns it alongside the nonce
    /// the caller must hand back to <c>HandleAuthenticateAsync</c> as
    /// <c>expectedNonce</c>. The new server flow requires the request nonce
    /// to exactly equal the server's pre-issued nonce, and the HMAC to cover
    /// (pid || imageHash || nonce).
    /// </summary>
    private (IpcMessage Request, byte[] Nonce) CreateValidAuthRequest()
    {
        var pid = Environment.ProcessId;
        var nonce = RandomNumberGenerator.GetBytes(32);
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Current executable path is unavailable.");
        var imageHash = ClientImageHasher.HashFile(executablePath);
        var signature = HmacAuthenticator.SignWithNonce(pid, imageHash, nonce, _secret!);

        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = Guid.NewGuid().ToString("N"),
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Signature = signature,
                ExecutablePath = executablePath,
                ClientImageHash = imageHash,
                Nonce = nonce,
            })
        };
        return (request, nonce);
    }

    private static DateTimeOffset FreshNonceExpiry() => DateTimeOffset.UtcNow.AddSeconds(30);

    /// <summary>
    /// Authenticates and returns the session ID for tests that need a valid session.
    /// </summary>
    private async Task<string> AuthenticateAndGetSession()
    {
        var (authReq, nonce) = CreateValidAuthRequest();
        var (_, sessionId) = await _server!.HandleAuthenticateAsync(
            authReq, Environment.ProcessId, nonce, FreshNonceExpiry(), CancellationToken.None);
        sessionId.Should().NotBeNull();
        return sessionId!;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HandleAuthenticate
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task HandleAuthenticate_ValidHmac_ReturnsSuccess()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var (request, nonce) = CreateValidAuthRequest();
        var (response, sessionId) = await _server!.HandleAuthenticateAsync(
            request, Environment.ProcessId, nonce, FreshNonceExpiry(), CancellationToken.None);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeTrue();
        authResp.SessionId.Should().NotBeNullOrEmpty();
        authResp.ExpiresAtUtc.Should().BeGreaterThan(0);
        authResp.ServerSignature.Should().NotBeNullOrEmpty();
        sessionId.Should().NotBeNull();
    }

    [Test]
    public async Task HandleAuthenticate_InvalidHmac_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        // Build a request with a valid nonce + image hash so the earlier
        // nonce/length checks pass, but a bogus signature so the HMAC check
        // is the actual failure point under test.
        var pid = Environment.ProcessId;
        var nonce = RandomNumberGenerator.GetBytes(32);
        var imageHash = ClientImageHasher.HashFile(Environment.ProcessPath!);
        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "test-1",
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Signature = "invalid-signature",
                ExecutablePath = Environment.ProcessPath!,
                ClientImageHash = imageHash,
                Nonce = nonce,
            })
        };

        var (response, sessionId) = await _server!.HandleAuthenticateAsync(
            request, Environment.ProcessId, nonce, FreshNonceExpiry(), CancellationToken.None);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Authentication failed");
        sessionId.Should().BeNull();
    }

    [Test]
    public async Task HandleAuthenticate_NonceMismatch_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        // Client signs and embeds one nonce, but the server expects a
        // different one (e.g. a replay of a captured request against a
        // fresh server-issued challenge).
        var (request, _) = CreateValidAuthRequest();
        var differentNonce = RandomNumberGenerator.GetBytes(32);

        var (response, sessionId) = await _server!.HandleAuthenticateAsync(
            request, Environment.ProcessId, differentNonce, FreshNonceExpiry(), CancellationToken.None);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Nonce verification failed");
        sessionId.Should().BeNull();
    }

    [Test]
    public async Task HandleAuthenticate_NonceExpired_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        // Nonce matches but the server's freshness window has already elapsed —
        // simulates a too-slow handshake or a replay against an expired challenge.
        var (request, nonce) = CreateValidAuthRequest();
        var expiredAt = DateTimeOffset.UtcNow.AddSeconds(-5);

        var (response, sessionId) = await _server!.HandleAuthenticateAsync(
            request, Environment.ProcessId, nonce, expiredAt, CancellationToken.None);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Nonce expired");
        sessionId.Should().BeNull();
    }

    [Test]
    public async Task HandleAuthenticate_MalformedPayload_ReturnsInvalidAuth()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "test-3",
            Payload = [0xFF, 0xFE] // Invalid MessagePack
        };

        var (response, sessionId) = await _server!.HandleAuthenticateAsync(
            request, Environment.ProcessId, RandomNumberGenerator.GetBytes(32), FreshNonceExpiry(), CancellationToken.None);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Invalid auth request");
        sessionId.Should().BeNull();
    }

    [Test]
    public async Task HandleAuthenticate_MissingExecutablePath_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        // Empty ExecutablePath should be rejected after nonce + HMAC pass.
        // (The old "WrongExecutablePath" test relied on path-prefix matching
        // inside the server; in the new flow the verifier owns that decision,
        // so a wrong-but-non-empty path is exercised by the IdentityVerifierRejects
        // test instead.)
        var pid = Environment.ProcessId;
        var nonce = RandomNumberGenerator.GetBytes(32);
        var imageHash = ClientImageHasher.HashFile(Environment.ProcessPath!);
        var signature = HmacAuthenticator.SignWithNonce(pid, imageHash, nonce, _secret!);

        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "test-exe",
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Signature = signature,
                ExecutablePath = string.Empty,
                ClientImageHash = imageHash,
                Nonce = nonce,
            })
        };

        var (response, sessionId) = await _server!.HandleAuthenticateAsync(
            request, pid, nonce, FreshNonceExpiry(), CancellationToken.None);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Executable path required");
        sessionId.Should().BeNull();
    }

    [Test]
    public async Task HandleAuthenticate_IdentityVerifierRejects_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        // Spin up a dedicated server whose verifier always rejects so we can
        // assert that an identity-verification failure surfaces as the right
        // wire-level error and does not create a session. The constructor
        // overwrites the on-disk secret atomically; the [NotInParallel]
        // contract makes this safe.
        var rejectingVerifier = new PassThroughClientIdentityVerifier(
            (_, _) => ClientIdentityResult.Invalid("test reject"));
        using var rejectingServer = new ElevationServer(rejectingVerifier);
        var rejectingSecret = (byte[])typeof(ElevationServer)
            .GetField("_secret", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(rejectingServer)!;

        var pid = Environment.ProcessId;
        var nonce = RandomNumberGenerator.GetBytes(32);
        var imageHash = ClientImageHasher.HashFile(Environment.ProcessPath!);
        var signature = HmacAuthenticator.SignWithNonce(pid, imageHash, nonce, rejectingSecret);

        var request = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "reject-id",
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Signature = signature,
                ExecutablePath = Environment.ProcessPath!,
                ClientImageHash = imageHash,
                Nonce = nonce,
            })
        };

        var (response, sessionId) = await rejectingServer.HandleAuthenticateAsync(
            request, pid, nonce, FreshNonceExpiry(), CancellationToken.None);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("Identity verification failed");
        sessionId.Should().BeNull();
    }

    [Test]
    public async Task HandleAuthenticate_PidMismatch_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var (request, nonce) = CreateValidAuthRequest();
        // Pass a peerPid that differs from Environment.ProcessId in the auth request
        var (response, sessionId) = await _server!.HandleAuthenticateAsync(
            request, Environment.ProcessId + 9999, nonce, FreshNonceExpiry(), CancellationToken.None);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeFalse();
        authResp.ErrorMessage.Should().Contain("PID verification failed");
        sessionId.Should().BeNull();
    }

    [Test]
    public async Task HandleAuthenticate_PeerPidZero_SkipsCrossVerification()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var (request, nonce) = CreateValidAuthRequest();
        // peerPid=0 means SO_PEERCRED failed, should skip cross-verification
        var (response, sessionId) = await _server!.HandleAuthenticateAsync(
            request, 0, nonce, FreshNonceExpiry(), CancellationToken.None);

        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
        authResp.Success.Should().BeTrue();
        authResp.SessionId.Should().NotBeNullOrEmpty();
        sessionId.Should().NotBeNull();
    }

    [Test]
    public async Task HandleAuthenticate_MaxSessionsExceeded_ReturnsFailed()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        // Each call uses a fresh nonce but the SAME image hash, so the
        // single-client bind treats every subsequent auth as a rebind and
        // permits it — letting us exhaust the session pool to test the
        // max-sessions branch.
        for (var i = 0; i < IpcConstants.MaxConcurrentSessions; i++)
        {
            var (req, nonce) = CreateValidAuthRequest();
            var (resp, _) = await _server!.HandleAuthenticateAsync(
                req, Environment.ProcessId, nonce, FreshNonceExpiry(), CancellationToken.None);
            var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(resp.Payload);
            authResp.Success.Should().BeTrue($"session {i} should succeed");
        }

        // The next should fail
        var (overflowReq, overflowNonce) = CreateValidAuthRequest();
        var (overflowResp, sid) = await _server!.HandleAuthenticateAsync(
            overflowReq, Environment.ProcessId, overflowNonce, FreshNonceExpiry(), CancellationToken.None);

        var overflowAuth = IpcTransport.DeserializePayload<AuthenticateResponse>(overflowResp.Payload);
        overflowAuth.Success.Should().BeFalse();
        overflowAuth.ErrorMessage.Should().Contain("Max sessions exceeded");
        sid.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HandleConnectionStats
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task HandleConnectionStats_WithValidSession_ReturnsSuccess()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var sessionId = await AuthenticateAndGetSession();

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
    public async Task HandleProcessStats_WithValidSession_ReturnsSuccess()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var sessionId = await AuthenticateAndGetSession();

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
    public async Task HandleProcessStats_MalformedPayload_ReturnsError()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var sessionId = await AuthenticateAndGetSession();

        var request = new IpcMessage
        {
            Type = MessageType.ProcessStats,
            RequestId = "proc-3",
            Payload = [0xFF, 0xFE] // Invalid
        };

        var response = _server!.HandleProcessStats(request, sessionId);
        // Hardened behavior (see 9f2c7a4): malformed payloads are rejected
        // outright rather than silently falling back to a default request.
        response.Type.Should().Be(MessageType.Error);
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
    public async Task HandleShutdown_WithValidSession_ReturnsResponse()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var sessionId = await AuthenticateAndGetSession();

        var request = new IpcMessage { Type = MessageType.Shutdown, RequestId = "sd-1" };
        var response = _server!.HandleShutdown(request, sessionId);

        response.Type.Should().Be(MessageType.Shutdown);
        var shutdownResp = IpcTransport.DeserializePayload<ShutdownResponse>(response.Payload);
        shutdownResp.Acknowledged.Should().BeTrue();
        shutdownResp.Reason.Should().Contain("shutdown");
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
    public async Task ValidateSession_ValidSession_ReturnsTrue()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        var sessionId = await AuthenticateAndGetSession();

        _server!.ValidateSession(sessionId, out var error).Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Test]
    public void ValidateSession_NonexistentSession_ReturnsFalse()
    {
        Skip.Unless(_available, "ElevationServer not available (wrong platform or secret file locked)");

        _server!.ValidateSession("nonexistent", out _).Should().BeFalse();
    }

    public void Dispose()
    {
        _server?.Dispose();
    }
}

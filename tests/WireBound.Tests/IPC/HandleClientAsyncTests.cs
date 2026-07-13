using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using WireBound.Elevation.Windows;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

/// <summary>
/// Tests the HandleClientAsync message loop in the Windows ElevationServer.
/// Uses a DuplexStream (two MemoryStreams) to simulate the IPC transport
/// without requiring named pipes or admin privileges.
///
/// <para>
/// The new auth flow makes the server push a server-issued nonce as its
/// first message (Challenge) before reading any client message. Tests
/// therefore need to:
/// <list type="number">
///   <item>Override the server's <c>NonceFactory</c> with a deterministic
///         nonce so the test can pre-compute its HMAC signature.</item>
///   <item>Expect the Challenge as the FIRST response in every test.</item>
///   <item>Use a <see cref="PassThroughClientIdentityVerifier"/> so the
///         test process (which is not WireBound.exe) passes identity
///         verification — separate from the auth tests in
///         <c>WindowsElevationServerHandlerTests</c>, here we want to test
///         message-loop behaviour, not the verifier itself.</item>
/// </list>
/// </para>
/// </summary>
[NotInParallel("SecretFile")]
[SupportedOSPlatform("windows")]
public class HandleClientAsyncTests : IDisposable
{
    private readonly ElevationServer? _server;
    private readonly byte[]? _secret;
    private readonly byte[] _fixedNonce;
    private readonly bool _available;

    public HandleClientAsyncTests()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User!.Value;
            _server = new ElevationServer(sid, new PassThroughClientIdentityVerifier());
            // Extract secret via reflection to avoid race with other test classes
            // that also call SecretManager.GenerateAndStore() concurrently
            _secret = (byte[])typeof(ElevationServer)
                .GetField("_secret", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(_server)!;

            // Fix the nonce so test-prepared auth messages can sign against it.
            // The 32-byte pattern is just a memorable test marker.
            _fixedNonce = new byte[32];
            for (var i = 0; i < 32; i++) _fixedNonce[i] = (byte)(0xA0 + i);
            _server.NonceFactory = () => _fixedNonce;

            _available = true;
        }
        catch (Exception) when (!OperatingSystem.IsWindows()) { _available = false; _fixedNonce = []; }
        catch (IOException) { _available = false; _fixedNonce = []; }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private IpcMessage CreateValidAuthMessage()
    {
        var pid = Environment.ProcessId;
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Current executable path is unavailable.");
        var imageHash = ClientImageHasher.HashFile(executablePath);
        return new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = Guid.NewGuid().ToString("N"),
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Signature = HmacAuthenticator.SignWithNonce(pid, imageHash, _fixedNonce, _secret!),
                ExecutablePath = executablePath,
                ClientImageHash = imageHash,
                Nonce = _fixedNonce
            })
        };
    }

    private static async Task<MemoryStream> PrepareClientMessages(params IpcMessage[] messages)
    {
        var ms = new MemoryStream();
        foreach (var msg in messages)
            await IpcTransport.SendAsync(ms, msg, CancellationToken.None);
        ms.Position = 0;
        return ms;
    }

    private static async Task<List<IpcMessage>> ReadResponses(MemoryStream serverOutput)
    {
        serverOutput.Position = 0;
        var responses = new List<IpcMessage>();
        while (serverOutput.Position < serverOutput.Length)
        {
            var msg = await IpcTransport.ReceiveAsync(serverOutput, CancellationToken.None);
            if (msg is null) break;
            responses.Add(msg);
        }
        return responses;
    }

    private async Task<List<IpcMessage>> RunClientLoop(params IpcMessage[] messages)
    {
        var clientToServer = await PrepareClientMessages(messages);
        var serverToClient = new MemoryStream();
        var duplex = new DuplexStream(clientToServer, serverToClient);

        await _server!.HandleClientAsync(duplex, Environment.ProcessId, CancellationToken.None);

        var all = await ReadResponses(serverToClient);

        // Drop the leading Challenge message — it is always emitted as the
        // first response on every new connection (nonce handshake). The tests
        // below assert on the subsequent message exchange.
        if (all.Count > 0 && all[0].Type == MessageType.Challenge)
        {
            // Sanity-check the nonce is what we forced via NonceFactory.
            var challenge = IpcTransport.DeserializePayload<ChallengeMessage>(all[0].Payload);
            challenge.Nonce.Should().Equal(_fixedNonce, "test seeded a deterministic nonce");
            all.RemoveAt(0);
        }
        return all;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Full message loop tests
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task HandleClientAsync_AuthThenShutdown_CompletesGracefully()
    {
        if (!_available) return;

        var auth = CreateValidAuthMessage();
        var shutdown = new IpcMessage { Type = MessageType.Shutdown, RequestId = "sd-1" };

        var responses = await RunClientLoop(auth, shutdown);

        responses.Count.Should().Be(2);
        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(responses[0].Payload);
        authResp.Success.Should().BeTrue();
        var sdResp = IpcTransport.DeserializePayload<ShutdownResponse>(responses[1].Payload);
        sdResp.Acknowledged.Should().BeTrue();
        sdResp.Reason.Should().Contain("shutdown");
    }

    [Test]
    public async Task HandleClientAsync_AuthThenStats_ReturnsStats()
    {
        if (!_available) return;

        var auth = CreateValidAuthMessage();
        var stats = new IpcMessage { Type = MessageType.ConnectionStats, RequestId = "cs-1" };

        var responses = await RunClientLoop(auth, stats);

        responses.Count.Should().Be(2);
        var statsResp = IpcTransport.DeserializePayload<ConnectionStatsResponse>(responses[1].Payload);
        statsResp.Success.Should().BeTrue();
    }

    [Test]
    public async Task HandleClientAsync_AuthThenProcessStats_ReturnsStats()
    {
        if (!_available) return;

        var auth = CreateValidAuthMessage();
        var stats = new IpcMessage
        {
            Type = MessageType.ProcessStats,
            RequestId = "ps-1",
            Payload = IpcTransport.SerializePayload(new ProcessStatsRequest())
        };

        var responses = await RunClientLoop(auth, stats);

        responses.Count.Should().Be(2);
        var statsResp = IpcTransport.DeserializePayload<ProcessStatsResponse>(responses[1].Payload);
        statsResp.Success.Should().BeTrue();
    }

    [Test]
    public async Task HandleClientAsync_AuthThenHeartbeat_ReturnsAlive()
    {
        if (!_available) return;

        var auth = CreateValidAuthMessage();
        var heartbeat = new IpcMessage { Type = MessageType.Heartbeat, RequestId = "hb-1" };

        var responses = await RunClientLoop(auth, heartbeat);

        responses.Count.Should().Be(2);
        var hbResp = IpcTransport.DeserializePayload<HeartbeatResponse>(responses[1].Payload);
        hbResp.Alive.Should().BeTrue();
        hbResp.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task HandleClientAsync_NoAuth_StatsRequest_ReturnsError()
    {
        if (!_available) return;

        var stats = new IpcMessage { Type = MessageType.ConnectionStats, RequestId = "cs-noauth" };

        var responses = await RunClientLoop(stats);

        responses.Count.Should().Be(1);
        responses[0].Type.Should().Be(MessageType.Error);
        var err = IpcTransport.DeserializePayload<ErrorResponse>(responses[0].Payload);
        err.Error.Should().Contain("Invalid or expired session");
    }

    [Test]
    public async Task HandleClientAsync_InvalidAuth_StatsRequest_BothFail()
    {
        if (!_available) return;

        // Build an auth with wrong signature but correct nonce/hash plumbing
        // so the test exercises the HMAC-validation branch, not the nonce or
        // schema-validation branches.
        var pid = Environment.ProcessId;
        var exePath = Environment.ProcessPath!;
        var imageHash = ClientImageHasher.HashFile(exePath);
        var badAuth = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = "bad-auth",
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Signature = "wrong-signature-not-base64-valid-hmac",
                ExecutablePath = exePath,
                ClientImageHash = imageHash,
                Nonce = _fixedNonce
            })
        };
        var stats = new IpcMessage { Type = MessageType.ConnectionStats, RequestId = "cs-2" };

        var responses = await RunClientLoop(badAuth, stats);

        responses.Count.Should().Be(2);
        // Auth fails
        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(responses[0].Payload);
        authResp.Success.Should().BeFalse();
        // Stats also fails (no session)
        responses[1].Type.Should().Be(MessageType.Error);
    }

    [Test]
    public async Task HandleClientAsync_StreamEnds_ExitsGracefully()
    {
        if (!_available) return;

        var auth = CreateValidAuthMessage();
        // Only auth, no shutdown — stream will end naturally
        var responses = await RunClientLoop(auth);

        responses.Count.Should().Be(1);
        var authResp = IpcTransport.DeserializePayload<AuthenticateResponse>(responses[0].Payload);
        authResp.Success.Should().BeTrue();
    }

    [Test]
    public async Task HandleClientAsync_EmptyStream_ExitsImmediately()
    {
        if (!_available) return;

        var responses = await RunClientLoop(); // No messages at all

        responses.Count.Should().Be(0);
    }

    [Test]
    public async Task HandleClientAsync_UnknownMessageType_ReturnsError()
    {
        if (!_available) return;

        var auth = CreateValidAuthMessage();
        var unknown = new IpcMessage
        {
            Type = (MessageType)99,
            RequestId = "unk-1"
        };

        var responses = await RunClientLoop(auth, unknown);

        responses.Count.Should().Be(2);
        responses[1].Type.Should().Be(MessageType.Error);
        var err = IpcTransport.DeserializePayload<ErrorResponse>(responses[1].Payload);
        err.Error.Should().Contain("Unknown message type");
    }

    [Test]
    public async Task HandleClientAsync_MultipleStatsRequests_AllSucceed()
    {
        if (!_available) return;

        var auth = CreateValidAuthMessage();
        var msgs = new List<IpcMessage> { auth };
        for (var i = 0; i < 5; i++)
            msgs.Add(new IpcMessage { Type = MessageType.ConnectionStats, RequestId = $"cs-{i}" });

        var responses = await RunClientLoop(msgs.ToArray());

        responses.Count.Should().Be(6);
        for (var i = 1; i < 6; i++)
        {
            var statsResp = IpcTransport.DeserializePayload<ConnectionStatsResponse>(responses[i].Payload);
            statsResp.Success.Should().BeTrue();
        }
    }

    [Test]
    public async Task HandleClientAsync_CancellationToken_ExitsGracefully()
    {
        if (!_available) return;

        // Create a stream that blocks on read after first message
        var auth = CreateValidAuthMessage();
        var clientToServer = await PrepareClientMessages(auth);
        var serverToClient = new MemoryStream();

        // Append a "blocking" read - the stream will end after auth, causing ReceiveAsync
        // to return null, which exits the loop. Test that cancellation doesn't throw.
        using var cts = new CancellationTokenSource();
        var duplex = new DuplexStream(clientToServer, serverToClient);

        // Cancel before it can process a second message
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        // Should not throw
        await _server!.HandleClientAsync(duplex, Environment.ProcessId, cts.Token);

        var responses = await ReadResponses(serverToClient);
        // At least the auth response should be there
        responses.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    public void Dispose()
    {
        _server?.Dispose();
    }
}

/// <summary>
/// A stream that reads from one backing stream and writes to another.
/// Used to test HandleClientAsync which reads client messages and writes responses on the same stream.
/// </summary>
internal sealed class DuplexStream : Stream
{
    private readonly Stream _readFrom;
    private readonly Stream _writeTo;

    public DuplexStream(Stream readFrom, Stream writeTo)
    {
        _readFrom = readFrom;
        _writeTo = writeTo;
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => _readFrom.Length;
    public override long Position
    {
        get => _readFrom.Position;
        set => _readFrom.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        _readFrom.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        _readFrom.ReadAsync(buffer, offset, count, ct);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        _readFrom.ReadAsync(buffer, ct);

    public override void Write(byte[] buffer, int offset, int count) =>
        _writeTo.Write(buffer, offset, count);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        _writeTo.WriteAsync(buffer, offset, count, ct);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
        _writeTo.WriteAsync(buffer, ct);

    public override void Flush() => _writeTo.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

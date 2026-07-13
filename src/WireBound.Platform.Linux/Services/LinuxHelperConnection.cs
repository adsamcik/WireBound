using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

[SupportedOSPlatform("linux")]
public sealed class LinuxHelperConnection : IHelperConnection
{
    private Socket? _socket;
    private NetworkStream? _stream;
    private string? _sessionId;

    public bool IsConnected => _socket is { Connected: true };

    public event EventHandler<HelperConnectionLostEventArgs>? ConnectionLost;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var endpoint = new UnixDomainSocketEndPoint(IpcConstants.LinuxSocketPath);
            await _socket.ConnectAsync(endpoint, cancellationToken);
            _stream = new NetworkStream(_socket, ownsSocket: false);

            var secret = SecretManager.Load();
            if (secret is null) return false;

            // STEP 1: receive the server's challenge nonce. The server sends
            // it immediately on connect — no client request required.
            var challengeMsg = await IpcTransport.ReceiveAsync(_stream, cancellationToken);
            if (challengeMsg is null || challengeMsg.Type != MessageType.Challenge)
            {
                Serilog.Log.Warning("Expected Challenge as first message, got {Type}", challengeMsg?.Type);
                return false;
            }
            var challenge = IpcTransport.DeserializePayload<ChallengeMessage>(challengeMsg.Payload);
            if (challenge.Nonce.Length == 0)
            {
                Serilog.Log.Warning("Challenge nonce is empty");
                return false;
            }

            // STEP 2: bind authentication to our own binary by hashing it now.
            var ownExePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Environment.ProcessPath is null");
            byte[] imageHash;
            try
            {
                imageHash = ClientImageHasher.HashFile(ownExePath);
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to hash own executable for IPC auth");
                return false;
            }

            // STEP 3: send AuthenticateRequest signed with HMAC(pid || hash || nonce).
            var pid = Environment.ProcessId;
            var signature = HmacAuthenticator.SignWithNonce(pid, imageHash, challenge.Nonce, secret);

            var authRequest = new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Signature = signature,
                ExecutablePath = ownExePath,
                ClientImageHash = imageHash,
                Nonce = challenge.Nonce
            };

            var request = new IpcMessage
            {
                Type = MessageType.Authenticate,
                Payload = IpcTransport.SerializePayload(authRequest)
            };

            await IpcTransport.SendAsync(_stream, request, cancellationToken);
            var response = await IpcTransport.ReceiveAsync(_stream, cancellationToken);
            if (response is null) return false;

            var authResponse = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
            if (!authResponse.Success) return false;

            // STEP 4: verify the server's mutual-auth signature is bound to
            // OUR nonce (defeats pipe-squatting even if the secret leaked).
            var expectedServerSig = HmacAuthenticator.SignServerResponse(
                authResponse.SessionId ?? string.Empty,
                authResponse.ExpiresAtUtc,
                challenge.Nonce,
                secret);
            if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expectedServerSig),
                System.Text.Encoding.UTF8.GetBytes(authResponse.ServerSignature)))
            {
                Serilog.Log.Warning("Server signature verification failed — possible socket squatting attack");
                return false;
            }

            _sessionId = authResponse.SessionId;
            return true;
        }
        catch (OperationCanceledException)
        {
            await DisconnectAsync();
            return false;
        }
        catch (Exception)
        {
            await DisconnectAsync();
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_stream is not null)
        {
            await _stream.DisposeAsync();
            _stream = null;
        }
        _socket?.Dispose();
        _socket = null;
        _sessionId = null;
    }

    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var stream = _stream;
        var socket = _socket;
        if (stream is null || socket is not { Connected: true })
            throw new InvalidOperationException("Not connected to helper");

        var messageType = request switch
        {
            ConnectionStatsRequest => MessageType.ConnectionStats,
            ProcessStatsRequest => MessageType.ProcessStats,
            HeartbeatRequest => MessageType.Heartbeat,
            ShutdownRequest => MessageType.Shutdown,
            _ => throw new ArgumentException($"Unknown request type: {typeof(TRequest).Name}")
        };

        var message = new IpcMessage
        {
            Type = messageType,
            Payload = IpcTransport.SerializePayload(request)
        };

        await IpcTransport.SendAsync(stream, message, cancellationToken);
        var response = await IpcTransport.ReceiveAsync(stream, cancellationToken);

        if (response is null)
        {
            ConnectionLost?.Invoke(this, new HelperConnectionLostEventArgs("Connection lost", false));
            throw new InvalidOperationException("Lost connection to helper");
        }

        return IpcTransport.DeserializePayload<TResponse>(response.Payload);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}

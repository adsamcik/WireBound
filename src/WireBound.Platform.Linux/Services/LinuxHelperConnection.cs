using System.Net.Sockets;
using System.Text.Json;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

public class LinuxHelperConnection : IHelperConnection
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

            var secret = ReadSecret();
            if (secret is null) return false;

            var pid = Environment.ProcessId;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signature = HmacAuthenticator.Sign(pid, timestamp, secret);

            var authRequest = new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = timestamp,
                Signature = signature
            };

            var request = new IpcMessage
            {
                Type = IpcConstants.AuthenticateType,
                Payload = JsonSerializer.Serialize(authRequest)
            };

            await IpcTransport.SendAsync(_stream, request, cancellationToken);
            var response = await IpcTransport.ReceiveAsync(_stream, cancellationToken);
            if (response is null) return false;

            var authResponse = JsonSerializer.Deserialize<AuthenticateResponse>(response.Payload);
            if (authResponse is null || !authResponse.Success) return false;

            _sessionId = authResponse.SessionId;
            return true;
        }
        catch
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
        if (_stream is null || _socket is not { Connected: true })
            throw new InvalidOperationException("Not connected to helper");

        var messageType = request switch
        {
            ConnectionStatsRequest => IpcConstants.ConnectionStatsType,
            HeartbeatRequest => IpcConstants.HeartbeatType,
            ShutdownRequest => IpcConstants.ShutdownType,
            _ => throw new ArgumentException($"Unknown request type: {typeof(TRequest).Name}")
        };

        var message = new IpcMessage
        {
            Type = messageType,
            Payload = JsonSerializer.Serialize(request)
        };

        await IpcTransport.SendAsync(_stream, message, cancellationToken);
        var response = await IpcTransport.ReceiveAsync(_stream, cancellationToken);

        if (response is null)
        {
            ConnectionLost?.Invoke(this, new HelperConnectionLostEventArgs("Connection lost", false));
            throw new InvalidOperationException("Lost connection to helper");
        }

        return JsonSerializer.Deserialize<TResponse>(response.Payload)
               ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    private static byte[]? ReadSecret()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WireBound", ".helper-secret");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}

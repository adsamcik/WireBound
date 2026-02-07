using System.IO.Pipes;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

public class WindowsHelperConnection : IHelperConnection
{
    private NamedPipeClientStream? _pipe;
    private string? _sessionId;

    public bool IsConnected => _pipe is { IsConnected: true };

    public event EventHandler<HelperConnectionLostEventArgs>? ConnectionLost;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _pipe = new NamedPipeClientStream(".", IpcConstants.WindowsPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync(5000, cancellationToken);

            var secret = SecretManager.Load();
            if (secret is null) return false;

            var pid = Environment.ProcessId;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var signature = HmacAuthenticator.Sign(pid, timestamp, secret);

            var authRequest = new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = timestamp,
                Signature = signature,
                ExecutablePath = Environment.ProcessPath ?? string.Empty
            };

            var request = new IpcMessage
            {
                Type = MessageType.Authenticate,
                Payload = IpcTransport.SerializePayload(authRequest)
            };

            await IpcTransport.SendAsync(_pipe, request, cancellationToken);
            var response = await IpcTransport.ReceiveAsync(_pipe, cancellationToken);
            if (response is null) return false;

            var authResponse = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
            if (!authResponse.Success) return false;

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
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync();
            _pipe = null;
        }
        _sessionId = null;
    }

    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
        var pipe = _pipe;
        if (pipe is null || !pipe.IsConnected)
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

        await IpcTransport.SendAsync(pipe, message, cancellationToken);
        var response = await IpcTransport.ReceiveAsync(pipe, cancellationToken);

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

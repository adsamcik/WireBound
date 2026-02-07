using System.IO.Pipes;
using System.Text.Json;
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

            await IpcTransport.SendAsync(_pipe, request, cancellationToken);
            var response = await IpcTransport.ReceiveAsync(_pipe, cancellationToken);
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
        if (_pipe is null || !_pipe.IsConnected)
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

        await IpcTransport.SendAsync(_pipe, message, cancellationToken);
        var response = await IpcTransport.ReceiveAsync(_pipe, cancellationToken);

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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(appData, "WireBound", ".helper-secret");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}

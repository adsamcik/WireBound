using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using MessagePack;
using Microsoft.Extensions.Logging;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Windows.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsHelperConnection : IHelperConnection
{
    private readonly ILogger<WindowsHelperConnection>? _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private string? _sessionId;
    private volatile bool _faulted;

    public WindowsHelperConnection(ILogger<WindowsHelperConnection>? logger = null)
    {
        _logger = logger;
    }

    public bool IsConnected => !_faulted && _pipe is { IsConnected: true };

    public event EventHandler<HelperConnectionLostEventArgs>? ConnectionLost;

    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            _faulted = false;
            await DisconnectCoreAsync();

            _pipe = new NamedPipeClientStream(".", IpcConstants.WindowsPipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync(5000, cancellationToken);

            var secret = SecretManager.Load();
            if (secret is null)
            {
                _logger?.LogError("Failed to load IPC authentication secret");
                await DisconnectCoreAsync();
                return false;
            }

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

            var authResponse = IpcTransport.DeserializePayload<AuthenticateResponse>(response.Payload);
            if (!authResponse.Success)
            {
                await DisconnectCoreAsync();
                return false;
            }

            // Verify server identity (mutual auth) — server must prove it holds the secret
            var expectedServerSig = HmacAuthenticator.Sign(0, authResponse.ExpiresAtUtc, secret);
            if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(expectedServerSig),
                System.Text.Encoding.UTF8.GetBytes(authResponse.ServerSignature)))
            {
                _logger?.LogWarning("Server signature verification failed — possible pipe squatting attack");
                await DisconnectCoreAsync();
                return false;
            }

            _sessionId = authResponse.SessionId;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await DisconnectCoreAsync();
            return false;
        }
        catch (IpcFramingException ex)
        {
            _logger?.LogError(ex, "IPC framing error during helper authentication");
            _faulted = true;
            await DisconnectCoreAsync();
            return false;
        }
        catch (MessagePackSerializationException ex)
        {
            _logger?.LogError(ex, "MessagePack serialization error during helper authentication");
            _faulted = true;
            await DisconnectCoreAsync();
            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to elevated helper");
            _faulted = true;
            await DisconnectCoreAsync();
            return false;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        await _ioLock.WaitAsync();
        try
        {
            await DisconnectCoreAsync();
            _faulted = false;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<TResponse> SendRequestAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : class
        where TResponse : class
    {
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

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            var pipe = _pipe;
            if (_faulted || pipe is null || !pipe.IsConnected)
                throw new InvalidOperationException("Not connected to helper");

            await IpcTransport.SendAsync(pipe, message, cancellationToken);
            var response = await IpcTransport.ReceiveAsync(pipe, cancellationToken);

            if (!string.Equals(response.RequestId, message.RequestId, StringComparison.Ordinal))
            {
                _faulted = true;
                await DisconnectCoreAsync();
                throw new InvalidOperationException("IPC response/request RequestId mismatch — fault injected");
            }

            return IpcTransport.DeserializePayload<TResponse>(response.Payload);
        }
        catch (IpcFramingException)
        {
            _faulted = true;
            await DisconnectCoreAsync();
            ConnectionLost?.Invoke(this, new HelperConnectionLostEventArgs("IPC framing error", false));
            throw;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _ioLock.Dispose();
    }

    private async Task DisconnectCoreAsync()
    {
        if (_pipe is not null)
        {
            await _pipe.DisposeAsync();
            _pipe = null;
        }

        _sessionId = null;
    }
}

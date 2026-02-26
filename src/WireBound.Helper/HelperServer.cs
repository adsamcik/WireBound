using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Serilog;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;

namespace WireBound.Helper;

/// <summary>
/// Main server loop that listens for IPC connections and handles requests.
/// Uses named pipes on Windows and Unix domain sockets on Linux.
/// </summary>
public sealed class HelperServer : IDisposable
{
    private readonly byte[] _secret;
    private readonly SessionManager _sessionManager = new();
    private readonly RateLimiter _rateLimiter = new();
    private readonly AuthRateLimiter _authRateLimiter = new();
    private readonly RateLimiter _heartbeatRateLimiter = new(1);
    private readonly ConnectionTracker _connectionTracker = new();

    public HelperServer()
    {
        _secret = SecretManager.GenerateAndStore();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await RunNamedPipeServerAsync(cancellationToken);
        }
        else if (OperatingSystem.IsLinux())
        {
            await RunUnixSocketServerAsync(cancellationToken);
        }
        else
        {
            Log.Error("Unsupported platform");
        }
    }

    [SupportedOSPlatform("windows")]
    private async Task RunNamedPipeServerAsync(CancellationToken cancellationToken)
    {
        Log.Information("Starting named pipe server: {Pipe}", IpcConstants.WindowsPipeName);

        while (!cancellationToken.IsCancellationRequested)
        {
            var server = CreateSecurePipe();

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                Log.Information("Client connected via named pipe");
                _ = HandleClientAsync(server, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error accepting pipe connection");
                await server.DisposeAsync();
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static NamedPipeServerStream CreateSecurePipe()
    {
        var security = new PipeSecurity();
        var currentUserSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows identity SID is unavailable.");
        security.AddAccessRule(new PipeAccessRule(
            currentUserSid,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            IpcConstants.WindowsPipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 0,
            outBufferSize: 0,
            security);
    }

    [SupportedOSPlatform("linux")]
    private async Task RunUnixSocketServerAsync(CancellationToken cancellationToken)
    {
        var socketDir = Path.GetDirectoryName(IpcConstants.LinuxSocketPath)!;
        if (!Directory.Exists(socketDir))
            Directory.CreateDirectory(socketDir);
        File.SetUnixFileMode(socketDir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        if (File.Exists(IpcConstants.LinuxSocketPath))
            File.Delete(IpcConstants.LinuxSocketPath);

        var endpoint = new UnixDomainSocketEndPoint(IpcConstants.LinuxSocketPath);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(endpoint);
        File.SetUnixFileMode(IpcConstants.LinuxSocketPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        listener.Listen(IpcConstants.MaxConcurrentSessions);

        Log.Information("Starting Unix socket server: {Path}", IpcConstants.LinuxSocketPath);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptAsync(cancellationToken);
                Log.Information("Client connected via Unix socket");
                _ = HandleClientAsync(new NetworkStream(client, ownsSocket: true), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error accepting socket connection");
            }
        }
    }

    private async Task HandleClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        string? sessionId = null;
        var clientId = stream.GetHashCode().ToString();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await IpcTransport.ReceiveAsync(stream, cancellationToken);
                if (message is null) break;

                if (sessionId is null && message.Type == MessageType.Authenticate && !_authRateLimiter.TryAcquire(clientId))
                {
                    var authRateLimitResponse = CreateResponse(message.RequestId, MessageType.Error,
                        new ErrorResponse { Error = "Auth rate limit exceeded" });
                    await IpcTransport.SendAsync(stream, authRateLimitResponse, cancellationToken);
                    continue;
                }

                // Rate limit authenticated sessions
                if (sessionId is not null && !_rateLimiter.TryAcquire(sessionId))
                {
                    var rateLimitResponse = CreateResponse(message.RequestId, MessageType.Error,
                        new ErrorResponse { Error = "Rate limit exceeded" });
                    await IpcTransport.SendAsync(stream, rateLimitResponse, cancellationToken);
                    continue;
                }

                if (message.Type == MessageType.Heartbeat)
                {
                    var heartbeatSource = sessionId ?? clientId;
                    if (!_heartbeatRateLimiter.TryAcquire(heartbeatSource))
                    {
                        var heartbeatRateLimitResp = CreateResponse(message.RequestId, MessageType.Error,
                            new ErrorResponse { Error = "Heartbeat rate limit exceeded" });
                        await IpcTransport.SendAsync(stream, heartbeatRateLimitResp, cancellationToken);
                        continue;
                    }
                }

                var response = message.Type switch
                {
                    MessageType.Authenticate => HandleAuthenticate(message, out sessionId),
                    MessageType.ConnectionStats => HandleConnectionStats(message, sessionId),
                    MessageType.ProcessStats => HandleProcessStats(message, sessionId),
                    MessageType.Heartbeat => HandleHeartbeat(),
                    MessageType.Shutdown => HandleShutdown(message, sessionId),
                    _ => CreateErrorResponse(message.RequestId, $"Unknown message type: {message.Type}")
                };

                // Track auth failures and disconnect after too many consecutive failures
                if (message.Type == MessageType.Authenticate)
                {
                    if (sessionId is null)
                    {
                        if (_authRateLimiter.RecordFailure(clientId))
                        {
                            Log.Warning("Client {ClientId} exceeded max consecutive auth failures, disconnecting", clientId);
                            await IpcTransport.SendAsync(stream, response, cancellationToken);
                            break;
                        }
                    }
                    else
                    {
                        _authRateLimiter.RecordSuccess(clientId);
                    }
                }

                await IpcTransport.SendAsync(stream, response, cancellationToken);

                if (message.Type == MessageType.Shutdown)
                    break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling client");
        }
        finally
        {
            if (sessionId is not null)
            {
                _sessionManager.RemoveSession(sessionId);
                _rateLimiter.RemoveClient(sessionId);
                _heartbeatRateLimiter.RemoveClient(sessionId);
            }
            _authRateLimiter.RemoveClient(clientId);
            _heartbeatRateLimiter.RemoveClient(clientId);
            if (stream is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private IpcMessage HandleAuthenticate(IpcMessage request, out string? sessionId)
    {
        sessionId = null;
        AuthenticateRequest authRequest;
        try
        {
            authRequest = IpcTransport.DeserializePayload<AuthenticateRequest>(request.Payload);
        }
        catch
        {
            return CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "Invalid auth request" });
        }

        if (!HmacAuthenticator.Validate(authRequest.ClientPid, authRequest.Timestamp, authRequest.Signature, _secret))
        {
            Log.Warning("Authentication failed for PID {Pid}", authRequest.ClientPid);
            return CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "Authentication failed" });
        }

        var session = _sessionManager.CreateSession(authRequest.ClientPid, authRequest.ExecutablePath);
        if (session is null)
        {
            return CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "Max sessions exceeded" });
        }

        sessionId = session.SessionId;
        Log.Information("Authenticated client PID {Pid}, session {SessionId}", authRequest.ClientPid, sessionId);

        return CreateResponse(request.RequestId, MessageType.Authenticate,
            new AuthenticateResponse
            {
                Success = true,
                SessionId = sessionId,
                ExpiresAtUtc = session.ExpiresAtUtc.ToUnixTimeSeconds()
            });
    }

    private IpcMessage HandleConnectionStats(IpcMessage request, string? sessionId)
    {
        if (!ValidateSession(sessionId, out var error))
            return CreateErrorResponse(request.RequestId, error);

        var stats = _connectionTracker.GetCurrentStats();
        return CreateResponse(request.RequestId, MessageType.ConnectionStats, stats);
    }

    private IpcMessage HandleProcessStats(IpcMessage request, string? sessionId)
    {
        if (!ValidateSession(sessionId, out var error))
            return CreateErrorResponse(request.RequestId, error);

        // ProcessStats aggregation will be implemented by platform-specific helpers
        return CreateResponse(request.RequestId, MessageType.ProcessStats,
            new ProcessStatsResponse { Success = true });
    }

    private IpcMessage HandleHeartbeat()
    {
        return CreateResponse(Guid.NewGuid().ToString("N"), MessageType.Heartbeat,
            new HeartbeatResponse
            {
                Alive = true
            });
    }

    private IpcMessage HandleShutdown(IpcMessage request, string? sessionId)
    {
        if (!ValidateSession(sessionId, out var error))
            return CreateErrorResponse(request.RequestId, error);

        Log.Information("Shutdown requested by session {SessionId}", sessionId);
        return CreateResponse(request.RequestId, MessageType.Shutdown,
            new HeartbeatResponse { Alive = false });
    }

    private bool ValidateSession(string? sessionId, out string error)
    {
        var session = _sessionManager.ValidateSession(sessionId);
        if (session is null)
        {
            error = "Invalid or expired session";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static IpcMessage CreateResponse<T>(string requestId, MessageType type, T payload)
    {
        return new IpcMessage
        {
            Type = type,
            RequestId = requestId,
            Payload = IpcTransport.SerializePayload(payload)
        };
    }

    private static IpcMessage CreateErrorResponse(string requestId, string error)
    {
        return new IpcMessage
        {
            Type = MessageType.Error,
            RequestId = requestId,
            Payload = IpcTransport.SerializePayload(new ErrorResponse { Error = error })
        };
    }

    public void Dispose()
    {
        _connectionTracker.Dispose();

        if (_secret.Length > 0)
            Array.Clear(_secret, 0, _secret.Length);

        SecretManager.Delete();
    }
}

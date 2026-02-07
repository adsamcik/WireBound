using System.Net.Sockets;
using Serilog;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;

namespace WireBound.Elevation.Linux;

/// <summary>
/// Elevated IPC server for Linux using Unix domain sockets with file permission protection.
/// Accepts authenticated clients and serves per-connection byte statistics via netlink SOCK_DIAG.
/// </summary>
public sealed class ElevationServer : IDisposable
{
    private readonly byte[] _secret;
    private readonly SessionManager _sessionManager = new();
    private readonly RateLimiter _rateLimiter = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly NetlinkConnectionTracker _tracker = new();

    public ElevationServer()
    {
        _secret = SecretManager.GenerateAndStore();
        Log.Information("Secret stored at {Path}", SecretManager.GetSecretFilePath());
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _tracker.Start();

        var socketDir = Path.GetDirectoryName(IpcConstants.LinuxSocketPath)!;
        if (!Directory.Exists(socketDir))
            Directory.CreateDirectory(socketDir);

        // Clean up stale socket
        if (File.Exists(IpcConstants.LinuxSocketPath))
            File.Delete(IpcConstants.LinuxSocketPath);

        var endpoint = new UnixDomainSocketEndPoint(IpcConstants.LinuxSocketPath);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(endpoint);

        // Set socket file permissions: owner read/write + group read/write (root:wirebound)
        File.SetUnixFileMode(IpcConstants.LinuxSocketPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite);

        listener.Listen(IpcConstants.MaxConcurrentSessions);
        Log.Information("Starting Unix socket server: {Path}", IpcConstants.LinuxSocketPath);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptAsync(cancellationToken);
                var clientPid = GetPeerPid(client);
                Log.Information("Client connected via Unix socket (PID: {Pid})", clientPid);
                _ = HandleClientAsync(new NetworkStream(client, ownsSocket: true), clientPid, cancellationToken);
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

        _tracker.Stop();

        // Clean up socket file
        try { File.Delete(IpcConstants.LinuxSocketPath); }
        catch { /* best effort */ }
    }

    /// <summary>
    /// Gets the peer process ID using SO_PEERCRED socket option.
    /// </summary>
    private static int GetPeerPid(Socket socket)
    {
        try
        {
            // SO_PEERCRED returns ucred struct: { pid, uid, gid } (3 ints = 12 bytes)
            var buffer = new byte[12];
            socket.GetRawSocketOption(1 /* SOL_SOCKET */, 17 /* SO_PEERCRED */, buffer);
            return BitConverter.ToInt32(buffer, 0); // First 4 bytes = pid
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get peer PID via SO_PEERCRED");
            return 0;
        }
    }

    private async Task HandleClientAsync(Stream stream, int peerPid, CancellationToken cancellationToken)
    {
        string? sessionId = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await IpcTransport.ReceiveAsync(stream, cancellationToken);
                if (message is null) break;

                if (sessionId is not null && !_rateLimiter.TryAcquire(sessionId))
                {
                    var rateLimitResp = CreateResponse(message.RequestId, MessageType.Error,
                        new ErrorResponse { Error = "Rate limit exceeded" });
                    await IpcTransport.SendAsync(stream, rateLimitResp, cancellationToken);
                    continue;
                }

                var response = message.Type switch
                {
                    MessageType.Authenticate => HandleAuthenticate(message, peerPid, out sessionId),
                    MessageType.ConnectionStats => HandleConnectionStats(message, sessionId),
                    MessageType.ProcessStats => HandleProcessStats(message, sessionId),
                    MessageType.Heartbeat => HandleHeartbeat(),
                    MessageType.Shutdown => HandleShutdown(message, sessionId),
                    _ => CreateErrorResponse(message.RequestId, $"Unknown message type: {message.Type}")
                };

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
            }
            if (stream is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private IpcMessage HandleAuthenticate(IpcMessage request, int peerPid, out string? sessionId)
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

        // Cross-verify: the PID in the auth request must match SO_PEERCRED PID
        if (peerPid > 0 && authRequest.ClientPid != peerPid)
        {
            Log.Warning("PID mismatch: claimed {ClaimedPid}, actual {ActualPid}",
                authRequest.ClientPid, peerPid);
            return CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "PID verification failed" });
        }

        if (!HmacAuthenticator.Validate(authRequest.ClientPid, authRequest.Timestamp, authRequest.Signature, _secret))
        {
            Log.Warning("Authentication failed for PID {Pid}", authRequest.ClientPid);
            return CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "Authentication failed" });
        }

        // Validate executable path via /proc/[pid]/exe
        if (!string.IsNullOrEmpty(authRequest.ExecutablePath))
        {
            if (!ValidateExecutablePath(authRequest.ExecutablePath, authRequest.ClientPid))
            {
                Log.Warning("Executable path validation failed for PID {Pid}: {Path}",
                    authRequest.ClientPid, authRequest.ExecutablePath);
                return CreateResponse(request.RequestId, MessageType.Authenticate,
                    new AuthenticateResponse { Success = false, ErrorMessage = "Executable validation failed" });
            }
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

        var stats = _tracker.GetConnectionStats();
        return CreateResponse(request.RequestId, MessageType.ConnectionStats, stats);
    }

    private IpcMessage HandleProcessStats(IpcMessage request, string? sessionId)
    {
        if (!ValidateSession(sessionId, out var error))
            return CreateErrorResponse(request.RequestId, error);

        ProcessStatsRequest statsRequest;
        try
        {
            statsRequest = IpcTransport.DeserializePayload<ProcessStatsRequest>(request.Payload);
        }
        catch
        {
            statsRequest = new ProcessStatsRequest();
        }

        var stats = _tracker.GetProcessStats(statsRequest.ProcessIds);
        return CreateResponse(request.RequestId, MessageType.ProcessStats, stats);
    }

    private IpcMessage HandleHeartbeat()
    {
        return CreateResponse(Guid.NewGuid().ToString("N"), MessageType.Heartbeat,
            new HeartbeatResponse
            {
                Alive = true,
                UptimeSeconds = (long)(DateTimeOffset.UtcNow - _startTime).TotalSeconds,
                ActiveSessions = _sessionManager.ActiveCount
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

    /// <summary>
    /// Validates that the client's executable path matches /proc/[pid]/exe symlink.
    /// </summary>
    private static bool ValidateExecutablePath(string claimedPath, int pid)
    {
        try
        {
            var procExe = $"/proc/{pid}/exe";
            if (!File.Exists(procExe)) return false;

            var actualPath = Path.GetFullPath(File.ResolveLinkTarget(procExe, returnFinalTarget: true)?.FullName ?? "");
            return string.Equals(
                Path.GetFullPath(claimedPath),
                actualPath,
                StringComparison.Ordinal);
        }
        catch
        {
            Log.Warning("Could not verify executable path for PID {Pid}", pid);
            return true; // Secret validation is primary auth
        }
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
        _tracker.Dispose();
        SecretManager.Delete();

        try { File.Delete(IpcConstants.LinuxSocketPath); }
        catch { /* best effort */ }
    }
}

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
    private readonly AuthRateLimiter _authRateLimiter = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly NetlinkConnectionTracker _tracker = new();
    private int _expectedPeerUid = -1;

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
        {
            Directory.CreateDirectory(socketDir);
            // Restrict directory: root-only access prevents socket path manipulation
            File.SetUnixFileMode(socketDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // Clean up stale socket
        if (File.Exists(IpcConstants.LinuxSocketPath))
            File.Delete(IpcConstants.LinuxSocketPath);

        // Determine the expected client UID before binding.
        // The elevation helper is launched by a regular user via pkexec/systemd,
        // so SUDO_UID (or PKEXEC_UID) tells us which user should be allowed to connect.
        _expectedPeerUid = ResolveExpectedPeerUid();
        if (_expectedPeerUid < 0)
        {
            Log.Error("Cannot determine expected peer UID — refusing to start without UID validation");
            throw new InvalidOperationException(
                "Unable to resolve the UID of the launching user. " +
                "Ensure the helper is launched via pkexec, sudo, or a systemd service with SUDO_UID/PKEXEC_UID set.");
        }
        Log.Information("Expected peer UID: {Uid}", _expectedPeerUid);

        var endpoint = new UnixDomainSocketEndPoint(IpcConstants.LinuxSocketPath);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(endpoint);

        // Set socket file permissions BEFORE listen() to eliminate the race window
        // where an unauthorized client could connect between bind() and chmod.
        // 0600: owner (root) read/write only — no group or other access.
        File.SetUnixFileMode(IpcConstants.LinuxSocketPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);

        listener.Listen(IpcConstants.MaxConcurrentSessions);
        Log.Information("Starting Unix socket server: {Path}", IpcConstants.LinuxSocketPath);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptAsync(cancellationToken);
                var (clientPid, clientUid) = GetPeerCredentials(client);

                // Verify the connecting user is the expected launching user
                if (clientUid != _expectedPeerUid)
                {
                    Log.Warning("Rejected connection from UID {ActualUid} (expected {ExpectedUid}, PID {Pid})",
                        clientUid, _expectedPeerUid, clientPid);
                    client.Dispose();
                    continue;
                }

                Log.Information("Client connected via Unix socket (PID: {Pid}, UID: {Uid})", clientPid, clientUid);
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
    /// Gets the peer process ID and user ID using SO_PEERCRED socket option.
    /// Returns (pid, uid) from the ucred struct.
    /// </summary>
    internal static (int Pid, int Uid) GetPeerCredentials(Socket socket)
    {
        try
        {
            // SO_PEERCRED returns ucred struct: { pid, uid, gid } (3 ints = 12 bytes)
            var buffer = new byte[12];
            socket.GetRawSocketOption(1 /* SOL_SOCKET */, 17 /* SO_PEERCRED */, buffer);
            var pid = BitConverter.ToInt32(buffer, 0);
            var uid = BitConverter.ToInt32(buffer, 4);
            return (pid, uid);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get peer credentials via SO_PEERCRED");
            return (0, -1);
        }
    }

    /// <summary>
    /// Determines the UID of the user who launched this elevated process.
    /// Checks SUDO_UID, PKEXEC_UID, then falls back to the owner of the secret file.
    /// </summary>
    internal static int ResolveExpectedPeerUid()
    {
        // pkexec sets PKEXEC_UID, sudo sets SUDO_UID
        var uidStr = Environment.GetEnvironmentVariable("PKEXEC_UID")
                  ?? Environment.GetEnvironmentVariable("SUDO_UID");

        if (uidStr is not null && int.TryParse(uidStr, out var uid))
            return uid;

        // Fallback: owner of the secret file (created by the unprivileged user)
        try
        {
            var secretPath = WireBound.IPC.Security.SecretManager.GetSecretFilePath();
            if (File.Exists(secretPath))
            {
                // Can't get owner UID from .NET File API alone; use /proc/self/loginuid
                var loginUidPath = "/proc/self/loginuid";
                if (File.Exists(loginUidPath))
                {
                    var loginUid = File.ReadAllText(loginUidPath).Trim();
                    if (int.TryParse(loginUid, out var loginUidValue) && loginUidValue >= 0)
                        return loginUidValue;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve expected peer UID from fallback");
        }

        Log.Warning("Could not determine expected peer UID from any source (PKEXEC_UID, SUDO_UID, loginuid)");
        return -1;
    }

    internal async Task HandleClientAsync(Stream stream, int peerPid, CancellationToken cancellationToken)
    {
        string? sessionId = null;
        var clientId = stream.GetHashCode().ToString();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await IpcTransport.ReceiveAsync(stream, cancellationToken);
                if (message is null) break;

                // Pre-auth rate limiting for authentication attempts
                if (sessionId is null && message.Type == MessageType.Authenticate)
                {
                    if (!_authRateLimiter.TryAcquire(clientId))
                    {
                        var rateLimitResp = CreateResponse(message.RequestId, MessageType.Error,
                            new ErrorResponse { Error = "Auth rate limit exceeded" });
                        await IpcTransport.SendAsync(stream, rateLimitResp, cancellationToken);
                        continue;
                    }
                }

                // Post-auth rate limiting for all other requests
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

                // Track auth failures and disconnect after too many consecutive failures
                if (message.Type == MessageType.Authenticate)
                {
                    if (sessionId is null)
                    {
                        if (_authRateLimiter.RecordFailure(clientId))
                        {
                            Log.Warning("Client {ClientId} (PID: {Pid}) exceeded max consecutive auth failures, disconnecting",
                                clientId, peerPid);
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
            }
            _authRateLimiter.RemoveClient(clientId);
            if (stream is IDisposable disposable)
                disposable.Dispose();
        }
    }

    internal IpcMessage HandleAuthenticate(IpcMessage request, int peerPid, out string? sessionId)
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

    internal IpcMessage HandleConnectionStats(IpcMessage request, string? sessionId)
    {
        if (!ValidateSession(sessionId, out var error))
            return CreateErrorResponse(request.RequestId, error);

        var stats = _tracker.GetConnectionStats();
        return CreateResponse(request.RequestId, MessageType.ConnectionStats, stats);
    }

    internal IpcMessage HandleProcessStats(IpcMessage request, string? sessionId)
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

    internal IpcMessage HandleHeartbeat()
    {
        return CreateResponse(Guid.NewGuid().ToString("N"), MessageType.Heartbeat,
            new HeartbeatResponse
            {
                Alive = true,
                UptimeSeconds = (long)(DateTimeOffset.UtcNow - _startTime).TotalSeconds,
                ActiveSessions = _sessionManager.ActiveCount
            });
    }

    internal IpcMessage HandleShutdown(IpcMessage request, string? sessionId)
    {
        if (!ValidateSession(sessionId, out var error))
            return CreateErrorResponse(request.RequestId, error);

        Log.Information("Shutdown requested by session {SessionId}", sessionId);
        return CreateResponse(request.RequestId, MessageType.Shutdown,
            new HeartbeatResponse { Alive = false });
    }

    internal bool ValidateSession(string? sessionId, out string error)
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
    internal static bool ValidateExecutablePath(string claimedPath, int pid)
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
            // Fail closed: if we can't verify, deny access
            Log.Warning("Could not verify executable path for PID {Pid}", pid);
            return false;
        }
    }

    internal static IpcMessage CreateResponse<T>(string requestId, MessageType type, T payload)
    {
        return new IpcMessage
        {
            Type = type,
            RequestId = requestId,
            Payload = IpcTransport.SerializePayload(payload)
        };
    }

    internal static IpcMessage CreateErrorResponse(string requestId, string error)
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

        // Zero the in-memory secret before releasing the reference
        if (_secret.Length > 0)
            Array.Clear(_secret, 0, _secret.Length);

        SecretManager.Delete();

        try { File.Delete(IpcConstants.LinuxSocketPath); }
        catch { /* best effort */ }
    }
}

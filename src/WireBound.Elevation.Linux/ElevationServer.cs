using System.Net.Sockets;
using System.Runtime.Versioning;
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
[SupportedOSPlatform("linux")]
public sealed class ElevationServer : IDisposable
{
    private readonly byte[] _secret;
    private readonly SessionManager _sessionManager = new();
    private readonly RateLimiter _rateLimiter = new();
    private readonly AuthRateLimiter _authRateLimiter = new();
    private readonly RateLimiter _heartbeatRateLimiter = new(1);
    private readonly NetlinkConnectionTracker _tracker = new();
    private readonly SemaphoreSlim _connectionSemaphore = new(20);
    private int _expectedPeerUid = -1;
    private FileStream? _lockFile;
    private string? _secretFilePath;

    public ElevationServer()
    {
        // Resolve the launching user's home directory so the secret is written
        // where the unprivileged client can read it (not in /root/.local/share/).
        var peerUid = ResolveExpectedPeerUid();
        if (peerUid >= 0)
        {
            var userHome = ResolveUserHome(peerUid);
            if (userHome is not null)
            {
                var secretPath = SecretManager.GetSecretFilePathForUser(userHome);
                var secretDir = Path.GetDirectoryName(secretPath)!;
                if (!Directory.Exists(secretDir))
                    Directory.CreateDirectory(secretDir);
                _secret = SecretManager.GenerateAndStore(secretDir);
                _secretFilePath = secretPath;
                // Chown the secret file to the launching user so they can read it
                ChownFile(secretPath, peerUid);
                Log.Information("Secret stored at {Path} (owned by UID {Uid})", secretPath, peerUid);
                _expectedPeerUid = peerUid;
                return;
            }
        }

        // Fallback: write to default path (may not work under pkexec but is fail-safe)
        _secret = SecretManager.GenerateAndStore();
        _secretFilePath = SecretManager.GetSecretFilePath();
        Log.Warning("Could not resolve user home; secret stored at default path {Path}", _secretFilePath);
        _expectedPeerUid = peerUid;
    }

    /// <summary>
    /// Resolves the home directory for a given UID by reading /etc/passwd.
    /// </summary>
    private static string? ResolveUserHome(int uid)
    {
        try
        {
            foreach (var line in File.ReadLines("/etc/passwd"))
            {
                var parts = line.Split(':');
                if (parts.Length >= 6 && int.TryParse(parts[2], out var entryUid) && entryUid == uid)
                    return parts[5]; // Home directory field
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to resolve home directory for UID {Uid}", uid);
        }
        return null;
    }

    /// <summary>
    /// Changes file ownership to the specified UID via /usr/bin/chown.
    /// </summary>
    private static void ChownFile(string path, int uid)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/chown",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(uid.ToString());
            startInfo.ArgumentList.Add(path);
            using var process = System.Diagnostics.Process.Start(startInfo);
            process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to chown secret file to UID {Uid}", uid);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _tracker.Start();

        var socketDir = Path.GetDirectoryName(IpcConstants.LinuxSocketPath)!;
        if (!Directory.Exists(socketDir))
        {
            Directory.CreateDirectory(socketDir);
            // Directory: 0711 — root-only read/write, but traversable by all users
            // (socket-level SO_PEERCRED UID validation provides actual access control)
            File.SetUnixFileMode(socketDir,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherExecute);
        }

        // Ensure single instance via lock file with crash recovery.
        // FileOptions.DeleteOnClose ensures the OS removes the file when the process exits
        // (even on crash/SIGKILL), preventing stale lock files from blocking restart.
        var lockPath = "/run/wirebound/elevation.lock";
        if (File.Exists(lockPath))
        {
            // Stale lock from a crash — try to remove it
            try { File.Delete(lockPath); }
            catch (IOException)
            {
                // Another live instance holds the file open — genuinely running
                Log.Error("Another instance of the elevation helper is already running (lock file held: {Path})", lockPath);
                throw new InvalidOperationException("Another instance is already running");
            }
        }
        try
        {
            _lockFile = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
        }
        catch (IOException)
        {
            Log.Error("Another instance of the elevation helper is already running (lock file exists: {Path})", lockPath);
            throw new InvalidOperationException("Another instance is already running");
        }

        // Clean up stale socket
        if (File.Exists(IpcConstants.LinuxSocketPath))
            File.Delete(IpcConstants.LinuxSocketPath);

        // Validate that we resolved the expected client UID in the constructor.
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

        // Set socket file permissions BEFORE listen() to eliminate the race window.
        // Socket: 0600 (owner-only) — then chown to the expected client UID.
        // This restricts connections to only the launching user at the filesystem level,
        // with SO_PEERCRED as defense-in-depth.
        File.SetUnixFileMode(IpcConstants.LinuxSocketPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        if (_expectedPeerUid >= 0)
            ChownFile(IpcConstants.LinuxSocketPath, _expectedPeerUid);

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
                if (!_connectionSemaphore.Wait(0))
                {
                    Log.Warning("Too many concurrent connections, rejecting (PID: {Pid})", clientPid);
                    client.Dispose();
                    continue;
                }
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
                    var loginUidRaw = File.ReadAllText(loginUidPath).Trim();
                    if (uint.TryParse(loginUidRaw, out var loginUidUint)
                        && loginUidUint != uint.MaxValue   // 0xFFFFFFFF = "no login UID set"
                        && loginUidUint <= int.MaxValue)   // safe to cast to int
                    {
                        return (int)loginUidUint;
                    }
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
        // Use verified peer PID as rate limiter identity (stable, OS-verified via SO_PEERCRED)
        var clientId = peerPid > 0 ? $"pid-{peerPid}" : stream.GetHashCode().ToString();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await IpcTransport.ReceiveAsync(stream, cancellationToken);
                if (message is null) break;

                // Rate-limit ALL messages from unauthenticated clients (not just Authenticate)
                if (sessionId is null)
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

                IpcMessage response;
                if (message.Type == MessageType.Authenticate)
                {
                    // Clean up prior session on re-auth to prevent session pool exhaustion
                    if (sessionId is not null)
                    {
                        _sessionManager.RemoveSession(sessionId);
                        _rateLimiter.RemoveClient(sessionId);
                        _heartbeatRateLimiter.RemoveClient(sessionId);
                    }
                    var (authResponse, newSessionId) = await HandleAuthenticateAsync(message, peerPid, cancellationToken);
                    sessionId = newSessionId;
                    response = authResponse;
                }
                else
                {
                    response = message.Type switch
                    {
                        MessageType.ConnectionStats => HandleConnectionStats(message, sessionId),
                        MessageType.ProcessStats => HandleProcessStats(message, sessionId),
                        MessageType.Heartbeat => HandleHeartbeat(),
                        MessageType.Shutdown => HandleShutdown(message, sessionId),
                        _ => CreateErrorResponse(message.RequestId, $"Unknown message type: {message.Type}")
                    };
                }

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
            _connectionSemaphore.Release();
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

    internal async Task<(IpcMessage Response, string? SessionId)> HandleAuthenticateAsync(
        IpcMessage request, int peerPid, CancellationToken cancellationToken)
    {
        AuthenticateRequest authRequest;
        try
        {
            authRequest = IpcTransport.DeserializePayload<AuthenticateRequest>(request.Payload);
        }
        catch
        {
            return (CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "Invalid auth request" }), null);
        }

        // Cross-verify: the PID in the auth request must match SO_PEERCRED PID
        if (peerPid > 0 && authRequest.ClientPid != peerPid)
        {
            Log.Warning("PID mismatch: claimed {ClaimedPid}, actual {ActualPid}",
                authRequest.ClientPid, peerPid);
            return (CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "PID verification failed" }), null);
        }

        if (!HmacAuthenticator.Validate(authRequest.ClientPid, authRequest.Timestamp, authRequest.Signature, _secret))
        {
            Log.Warning("Authentication failed for PID {Pid}", authRequest.ClientPid);
            return (CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "Authentication failed" }), null);
        }

        if (string.IsNullOrWhiteSpace(authRequest.ExecutablePath))
        {
            Log.Warning("Auth request from PID {Pid} missing executable path", authRequest.ClientPid);
            return (CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "Executable path required" }), null);
        }

        if (!ValidateExecutablePath(authRequest.ExecutablePath, authRequest.ClientPid))
        {
            Log.Warning("Executable path validation failed for PID {Pid}: {Path}",
                authRequest.ClientPid, authRequest.ExecutablePath);
            return (CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "Executable validation failed" }), null);
        }

        var session = await _sessionManager.CreateSessionAsync(
            authRequest.ClientPid, authRequest.ExecutablePath, cancellationToken);
        if (session is null)
        {
            return (CreateResponse(request.RequestId, MessageType.Authenticate,
                new AuthenticateResponse { Success = false, ErrorMessage = "Max sessions exceeded" }), null);
        }

        Log.Information("Authenticated client PID {Pid}, session {SessionId}", authRequest.ClientPid, session.SessionId);

        // Server proves it holds the secret (mutual auth)
        var serverSig = HmacAuthenticator.Sign(0, session.ExpiresAtUtc.ToUnixTimeSeconds(), _secret);

        return (CreateResponse(request.RequestId, MessageType.Authenticate,
            new AuthenticateResponse
            {
                Success = true,
                SessionId = session.SessionId,
                ExpiresAtUtc = session.ExpiresAtUtc.ToUnixTimeSeconds(),
                ServerSignature = serverSig
            }), session.SessionId);
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
            return CreateErrorResponse(request.RequestId, "Invalid process stats request");
        }

        // Cap PID filter list to prevent O(n×m) quadratic DoS
        const int maxFilterPids = 1000;
        if (statsRequest.ProcessIds.Count > maxFilterPids)
            return CreateErrorResponse(request.RequestId, $"Too many PIDs in filter (max {maxFilterPids})");

        var stats = _tracker.GetProcessStats(statsRequest.ProcessIds);
        return CreateResponse(request.RequestId, MessageType.ProcessStats, stats);
    }

    internal IpcMessage HandleHeartbeat()
    {
        return CreateResponse(Guid.NewGuid().ToString("N"), MessageType.Heartbeat,
            new HeartbeatResponse
            {
                Alive = true
            });
    }

    internal IpcMessage HandleShutdown(IpcMessage request, string? sessionId)
    {
        if (!ValidateSession(sessionId, out var error))
            return CreateErrorResponse(request.RequestId, error);

        Log.Information("Shutdown requested by session {SessionId}", sessionId);
        return CreateResponse(request.RequestId, MessageType.Shutdown,
            new ShutdownResponse { Acknowledged = true, Reason = "Client requested shutdown" });
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

        // Delete the actual secret file (may be at a custom user-home path, not the default)
        if (_secretFilePath is not null && File.Exists(_secretFilePath))
        {
            try
            {
                var zeros = new byte[32];
                File.WriteAllBytes(_secretFilePath, zeros);
                File.Delete(_secretFilePath);
            }
            catch { /* best effort */ }
        }
        else
        {
            SecretManager.Delete();
        }

        try { File.Delete(IpcConstants.LinuxSocketPath); }
        catch { /* best effort */ }

        try
        {
            _lockFile?.Dispose();
            File.Delete("/run/wirebound/elevation.lock");
        }
        catch { /* best effort */ }
    }
}

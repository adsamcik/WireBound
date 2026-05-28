using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using Serilog;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;

namespace WireBound.Elevation.Windows;

/// <summary>
/// Elevated IPC server for Windows using named pipes with ACL protection.
/// Accepts authenticated clients and serves per-connection byte statistics via ETW.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ElevationServer : IDisposable
{
    private readonly byte[] _secret;
    private readonly SecurityIdentifier _callerSid;
    private readonly SessionManager _sessionManager = new();
    private readonly RateLimiter _rateLimiter = new();
    private readonly AuthRateLimiter _authRateLimiter = new();
    private readonly RateLimiter _heartbeatRateLimiter = new(1);
    private readonly EtwConnectionTracker _tracker = new();
    private readonly SemaphoreSlim _connectionSemaphore = new(20); // Max 20 concurrent handshakes

    /// <summary>
    /// Creates the elevation server, granting pipe access to the specified caller SID.
    /// </summary>
    /// <param name="callerSid">
    /// The SID of the non-elevated user who launched this helper.
    /// Must be a valid SID string (e.g. "S-1-5-21-..."). Used in the named pipe ACL
    /// so the non-elevated main app can connect.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when callerSid is invalid.</exception>
    public ElevationServer(string callerSid)
    {
        _callerSid = ValidateAndParseSid(callerSid);
        _secret = SecretManager.GenerateAndStore();
        Log.Information("Secret stored at {Path}", SecretManager.GetSecretFilePath());
        Log.Information("Pipe ACL grants access to caller SID: {Sid}", _callerSid.Value);
    }

    /// <summary>
    /// Validates a SID string for format correctness and security constraints.
    /// Rejects well-known dangerous SIDs (Everyone, Anonymous, World).
    /// </summary>
    internal static SecurityIdentifier ValidateAndParseSid(string sidString)
    {
        if (string.IsNullOrWhiteSpace(sidString))
            throw new ArgumentException("Caller SID must not be empty.", nameof(sidString));

        // Strict format validation: only allow characters valid in a SID string
        if (!SidFormatRegex().IsMatch(sidString))
            throw new ArgumentException($"Caller SID has invalid format: {sidString}", nameof(sidString));

        SecurityIdentifier sid;
        try
        {
            sid = new SecurityIdentifier(sidString);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException($"Caller SID is not a valid Windows SID: {sidString}", nameof(sidString));
        }

        // Reject overly broad or dangerous well-known SIDs
        if (sid.IsWellKnown(WellKnownSidType.WorldSid) ||            // S-1-1-0  (Everyone)
            sid.IsWellKnown(WellKnownSidType.AnonymousSid) ||         // S-1-5-7  (Anonymous)
            sid.IsWellKnown(WellKnownSidType.AuthenticatedUserSid) || // S-1-5-11 (Authenticated Users)
            sid.IsWellKnown(WellKnownSidType.NetworkSid))             // S-1-5-2  (Network)
        {
            throw new ArgumentException(
                $"Caller SID must identify a specific user account, not a broad group: {sidString}",
                nameof(sidString));
        }

        return sid;
    }

    /// <summary>
    /// Matches a valid SID format: S-1-{authority}-{sub-authorities...}
    /// Only digits, hyphens, and the leading 'S' are permitted.
    /// </summary>
    [GeneratedRegex(@"^S-1-\d{1,14}(-\d{1,10}){1,15}$", RegexOptions.Compiled)]
    private static partial Regex SidFormatRegex();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var mutex = new Mutex(true, @"Global\WireBound.Elevation.Instance", out var createdNew);
        if (!createdNew)
        {
            Log.Error("Another instance of the elevation helper is already running");
            throw new InvalidOperationException("Another instance is already running");
        }

        _tracker.Start();
        Log.Information("Starting named pipe server: {Pipe}", IpcConstants.WindowsPipeName);

        while (!cancellationToken.IsCancellationRequested)
        {
            var server = CreateSecurePipe();

            try
            {
                await server.WaitForConnectionAsync(cancellationToken);
                var clientPid = GetClientPid(server);
                Log.Information("Client connected via named pipe (PID: {Pid})", clientPid);

                if (!_connectionSemaphore.Wait(0))
                {
                    Log.Warning("Too many concurrent connections, rejecting (PID: {Pid})", clientPid);
                    await server.DisposeAsync();
                    continue;
                }
                _ = HandleClientAsync(server, clientPid, cancellationToken);
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

        _tracker.Stop();
    }

    /// <summary>
    /// Creates a named pipe with restrictive ACL:
    /// - SYSTEM: Full control (required for the elevated service)
    /// - Administrators: Full control (the helper runs elevated via UAC and needs
    ///   CreateNewInstance to create subsequent pipe instances in the server loop)
    /// - Launching user (callerSid): Read/Write (so the non-elevated app can connect)
    /// </summary>
    private NamedPipeServerStream CreateSecurePipe()
    {
        var security = new PipeSecurity();

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // The elevated helper runs as admin via UAC (not SYSTEM). It needs
        // CreateNewInstance to create additional pipe instances in the accept loop.
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        // Allow the launching user (non-elevated) to connect
        security.AddAccessRule(new PipeAccessRule(
            _callerSid,
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

    internal static int GetClientPid(NamedPipeServerStream server)
    {
        try
        {
            return GetNamedPipeClientProcessId(server.SafePipeHandle, out var pid) ? (int)pid : 0;
        }
        catch
        {
            return 0;
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);

    internal async Task HandleClientAsync(Stream stream, int peerPid, CancellationToken cancellationToken)
    {
        string? sessionId = null;
        // Use verified pipe PID as rate limiter identity (stable across reconnections)
        var clientId = peerPid > 0 ? $"pid-{peerPid}" : stream.GetHashCode().ToString();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await IpcTransport.ReceiveAsync(
                    stream,
                    cancellationToken,
                    timeout: Timeout.InfiniteTimeSpan);

                // Pre-auth rate limiting for ALL messages from unauthenticated clients
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
        catch (IpcFramingException ex)
        {
            Log.Error(ex, "IPC framing error from PID {Pid} — closing connection", peerPid);
            return;
        }
        catch (System.IO.EndOfStreamException)
        {
            Log.Debug("Client disconnected (EOF) PID {Pid}", peerPid);
            return;
        }
        catch (System.IO.IOException ex) when (ex.InnerException is null or System.Net.Sockets.SocketException)
        {
            Log.Debug("Client connection dropped PID {Pid}: {Message}", peerPid, ex.Message);
            return;
        }
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

        // Cross-verify: the PID in the auth request must match the actual named pipe client PID
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

        var sessionId = session.SessionId;
        Log.Information("Authenticated client PID {Pid}, session {SessionId}", authRequest.ClientPid, sessionId);

        // Server proves it holds the secret (mutual auth)
        var serverSig = HmacAuthenticator.Sign(0, session.ExpiresAtUtc.ToUnixTimeSeconds(), _secret);

        return (CreateResponse(request.RequestId, MessageType.Authenticate,
            new AuthenticateResponse
            {
                Success = true,
                SessionId = sessionId,
                ExpiresAtUtc = session.ExpiresAtUtc.ToUnixTimeSeconds(),
                ServerSignature = serverSig
            }), sessionId);
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
    /// Validates that the client's executable path matches the expected WireBound application.
    /// </summary>
    internal static bool ValidateExecutablePath(string executablePath, int pid)
    {
        try
        {
            // Verify the process exists and its main module matches the claimed path
            var process = System.Diagnostics.Process.GetProcessById(pid);
            var actualPath = process.MainModule?.FileName;
            if (actualPath is null) return false;

            return string.Equals(
                Path.GetFullPath(actualPath),
                Path.GetFullPath(executablePath),
                StringComparison.OrdinalIgnoreCase);
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
    }
}

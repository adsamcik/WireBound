using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
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
public sealed partial class ElevationServer : IDisposable
{
    private readonly byte[] _secret;
    private readonly SecurityIdentifier _callerSid;
    private readonly SessionManager _sessionManager = new();
    private readonly RateLimiter _rateLimiter = new();
    private readonly AuthRateLimiter _authRateLimiter = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly EtwConnectionTracker _tracker = new();

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

        _tracker.Stop();
    }

    /// <summary>
    /// Creates a named pipe with restrictive ACL:
    /// - SYSTEM: Full control (required for the elevated service)
    /// - Launching user (callerSid): Read/Write (so the non-elevated app can connect)
    /// No other principals (including Administrators) get access — least privilege.
    /// </summary>
    private NamedPipeServerStream CreateSecurePipe()
    {
        var security = new PipeSecurity();

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
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

    private static int GetClientPid(NamedPipeServerStream server)
    {
        try
        {
            // GetClientProcessId is available on Windows
            return (int)server.GetType()
                .GetMethod("GetClientProcessId",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                .Invoke(server, null)!;
        }
        catch
        {
            return 0;
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
            }
            _authRateLimiter.RemoveClient(clientId);
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

        // Validate executable path if provided
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
    /// Validates that the client's executable path matches the expected WireBound application.
    /// </summary>
    private static bool ValidateExecutablePath(string executablePath, int pid)
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

        // Zero the in-memory secret before releasing the reference
        if (_secret.Length > 0)
            Array.Clear(_secret, 0, _secret.Length);

        SecretManager.Delete();
    }
}

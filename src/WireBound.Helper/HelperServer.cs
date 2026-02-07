using System.IO.Pipes;
using System.Net.Sockets;
using System.Text.Json;
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
public class HelperServer : IDisposable
{
    private readonly byte[] _secret = HmacAuthenticator.GenerateSecret();
    private readonly Dictionary<string, SessionInfo> _sessions = new();
    private readonly DateTimeOffset _startTime = DateTimeOffset.UtcNow;
    private readonly ConnectionTracker _connectionTracker = new();

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Write the secret to a temp file that the parent process can read
        WriteSecretFile();

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

    private void WriteSecretFile()
    {
        var secretPath = GetSecretFilePath();
        var dir = Path.GetDirectoryName(secretPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(secretPath, _secret);
        Log.Information("Secret written to {Path}", secretPath);
    }

    internal static string GetSecretFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "WireBound", ".helper-secret");
    }

    private async Task RunNamedPipeServerAsync(CancellationToken cancellationToken)
    {
        Log.Information("Starting named pipe server: {Pipe}", IpcConstants.WindowsPipeName);

        while (!cancellationToken.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream(
                IpcConstants.WindowsPipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

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

    private async Task RunUnixSocketServerAsync(CancellationToken cancellationToken)
    {
        var socketDir = Path.GetDirectoryName(IpcConstants.LinuxSocketPath)!;
        if (!Directory.Exists(socketDir))
            Directory.CreateDirectory(socketDir);

        if (File.Exists(IpcConstants.LinuxSocketPath))
            File.Delete(IpcConstants.LinuxSocketPath);

        var endpoint = new UnixDomainSocketEndPoint(IpcConstants.LinuxSocketPath);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(endpoint);
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
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await IpcTransport.ReceiveAsync(stream, cancellationToken);
                if (message is null) break;

                var response = message.Type switch
                {
                    IpcConstants.AuthenticateType => HandleAuthenticate(message, out sessionId),
                    IpcConstants.ConnectionStatsType => HandleConnectionStats(message, sessionId),
                    IpcConstants.HeartbeatType => HandleHeartbeat(sessionId),
                    IpcConstants.ShutdownType => HandleShutdown(message, sessionId),
                    _ => CreateErrorResponse(message.RequestId, $"Unknown message type: {message.Type}")
                };

                await IpcTransport.SendAsync(stream, response, cancellationToken);

                if (message.Type == IpcConstants.ShutdownType)
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
            if (sessionId != null)
                _sessions.Remove(sessionId);
            if (stream is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private IpcMessage HandleAuthenticate(IpcMessage request, out string? sessionId)
    {
        sessionId = null;
        var authRequest = JsonSerializer.Deserialize<AuthenticateRequest>(request.Payload);
        if (authRequest is null)
            return CreateErrorResponse(request.RequestId, "Invalid auth request");

        if (!HmacAuthenticator.Validate(authRequest.ClientPid, authRequest.Timestamp, authRequest.Signature, _secret))
        {
            Log.Warning("Authentication failed for PID {Pid}", authRequest.ClientPid);
            return CreateResponse(request.RequestId, IpcConstants.AuthenticateType,
                new AuthenticateResponse { Success = false, ErrorMessage = "Authentication failed" });
        }

        if (_sessions.Count >= IpcConstants.MaxConcurrentSessions)
        {
            return CreateResponse(request.RequestId, IpcConstants.AuthenticateType,
                new AuthenticateResponse { Success = false, ErrorMessage = "Max sessions exceeded" });
        }

        sessionId = Guid.NewGuid().ToString("N");
        _sessions[sessionId] = new SessionInfo(authRequest.ClientPid, DateTimeOffset.UtcNow);
        Log.Information("Authenticated client PID {Pid}, session {SessionId}", authRequest.ClientPid, sessionId);

        return CreateResponse(request.RequestId, IpcConstants.AuthenticateType,
            new AuthenticateResponse { Success = true, SessionId = sessionId });
    }

    private IpcMessage HandleConnectionStats(IpcMessage request, string? sessionId)
    {
        if (!ValidateSession(sessionId, out var error))
            return CreateErrorResponse(request.RequestId, error);

        var stats = _connectionTracker.GetCurrentStats();
        return CreateResponse(request.RequestId, IpcConstants.ConnectionStatsType, stats);
    }

    private IpcMessage HandleHeartbeat(string? sessionId)
    {
        return CreateResponse(Guid.NewGuid().ToString("N"), IpcConstants.HeartbeatType,
            new HeartbeatResponse
            {
                Alive = true,
                Uptime = DateTimeOffset.UtcNow - _startTime,
                ActiveSessions = _sessions.Count
            });
    }

    private IpcMessage HandleShutdown(IpcMessage request, string? sessionId)
    {
        if (!ValidateSession(sessionId, out var error))
            return CreateErrorResponse(request.RequestId, error);

        Log.Information("Shutdown requested by session {SessionId}", sessionId);
        return CreateResponse(request.RequestId, IpcConstants.ShutdownType,
            new { Success = true });
    }

    private bool ValidateSession(string? sessionId, out string error)
    {
        if (sessionId is null || !_sessions.TryGetValue(sessionId, out var session))
        {
            error = "Invalid or expired session";
            return false;
        }

        if (DateTimeOffset.UtcNow - session.CreatedAt > IpcConstants.MaxSessionDuration)
        {
            _sessions.Remove(sessionId);
            error = "Session expired";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static IpcMessage CreateResponse<T>(string requestId, string type, T payload)
    {
        return new IpcMessage
        {
            Type = type,
            RequestId = requestId,
            Payload = JsonSerializer.Serialize(payload)
        };
    }

    private static IpcMessage CreateErrorResponse(string requestId, string error)
    {
        return new IpcMessage
        {
            Type = "error",
            RequestId = requestId,
            Payload = JsonSerializer.Serialize(new { Error = error })
        };
    }

    public void Dispose()
    {
        _connectionTracker.Dispose();
        // Clean up secret file
        try
        {
            var secretPath = GetSecretFilePath();
            if (File.Exists(secretPath))
                File.Delete(secretPath);
        }
        catch { /* best effort cleanup */ }
    }

    private record SessionInfo(int ClientPid, DateTimeOffset CreatedAt);
}

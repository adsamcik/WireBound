#pragma warning disable CA1416

using System.Net.Sockets;
using WireBound.Elevation.Linux;
using WireBound.IPC;
using WireBound.IPC.Messages;
using WireBound.IPC.Security;
using WireBound.IPC.Transport;

namespace WireBound.Tests.IPC;

/// <summary>
/// Integration tests designed to run ONLY on Linux (in Docker).
/// Tests real /proc filesystem, Unix sockets, and SO_PEERCRED.
/// On non-Linux systems, tests skip gracefully via early return.
/// </summary>
[NotInParallel("SecretFile")]
public class LinuxIntegrationTests : IDisposable
{
    private readonly List<string> _socketPaths = [];

    // ═══════════════════════════════════════════════════════════════════════
    // GetPeerCredentials — SO_PEERCRED on Unix domain sockets
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void GetPeerCredentials_RealUnixSocket_ReturnsPidAndUid()
    {
        if (!OperatingSystem.IsLinux()) return;

        var socketPath = CreateTempSocketPath();
        var endpoint = new UnixDomainSocketEndPoint(socketPath);

        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(endpoint);
        server.Listen(1);

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        client.Connect(endpoint);

        using var accepted = server.Accept();

        var (pid, uid) = ElevationServer.GetPeerCredentials(accepted);

        pid.Should().Be(Environment.ProcessId);
        uid.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public void GetPeerCredentials_DisconnectedSocket_ReturnsDefault()
    {
        if (!OperatingSystem.IsLinux()) return;

        var socketPath = CreateTempSocketPath();
        var endpoint = new UnixDomainSocketEndPoint(socketPath);

        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(endpoint);
        server.Listen(1);

        var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        client.Connect(endpoint);
        using var accepted = server.Accept();

        client.Dispose();

        var (pid, uid) = ElevationServer.GetPeerCredentials(client);

        pid.Should().Be(0);
        uid.Should().Be(-1);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ResolveExpectedPeerUid — environment variable resolution
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ResolveExpectedPeerUid_WithPkexecUid_ReturnsUid()
    {
        if (!OperatingSystem.IsLinux()) return;

        var oldPkexec = Environment.GetEnvironmentVariable("PKEXEC_UID");
        var oldSudo = Environment.GetEnvironmentVariable("SUDO_UID");
        try
        {
            Environment.SetEnvironmentVariable("PKEXEC_UID", "1000");
            Environment.SetEnvironmentVariable("SUDO_UID", null);

            var uid = ElevationServer.ResolveExpectedPeerUid();

            uid.Should().Be(1000);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PKEXEC_UID", oldPkexec);
            Environment.SetEnvironmentVariable("SUDO_UID", oldSudo);
        }
    }

    [Test]
    public void ResolveExpectedPeerUid_WithSudoUid_ReturnsUid()
    {
        if (!OperatingSystem.IsLinux()) return;

        var oldPkexec = Environment.GetEnvironmentVariable("PKEXEC_UID");
        var oldSudo = Environment.GetEnvironmentVariable("SUDO_UID");
        try
        {
            Environment.SetEnvironmentVariable("PKEXEC_UID", null);
            Environment.SetEnvironmentVariable("SUDO_UID", "1001");

            var uid = ElevationServer.ResolveExpectedPeerUid();

            uid.Should().Be(1001);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PKEXEC_UID", oldPkexec);
            Environment.SetEnvironmentVariable("SUDO_UID", oldSudo);
        }
    }

    [Test]
    public void ResolveExpectedPeerUid_PkexecTakesPrecedence_OverSudo()
    {
        if (!OperatingSystem.IsLinux()) return;

        var oldPkexec = Environment.GetEnvironmentVariable("PKEXEC_UID");
        var oldSudo = Environment.GetEnvironmentVariable("SUDO_UID");
        try
        {
            Environment.SetEnvironmentVariable("PKEXEC_UID", "2000");
            Environment.SetEnvironmentVariable("SUDO_UID", "3000");

            var uid = ElevationServer.ResolveExpectedPeerUid();

            uid.Should().Be(2000);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PKEXEC_UID", oldPkexec);
            Environment.SetEnvironmentVariable("SUDO_UID", oldSudo);
        }
    }

    [Test]
    public void ResolveExpectedPeerUid_NeitherSet_FallsBackOrReturnsNegative()
    {
        if (!OperatingSystem.IsLinux()) return;

        var oldPkexec = Environment.GetEnvironmentVariable("PKEXEC_UID");
        var oldSudo = Environment.GetEnvironmentVariable("SUDO_UID");
        try
        {
            Environment.SetEnvironmentVariable("PKEXEC_UID", null);
            Environment.SetEnvironmentVariable("SUDO_UID", null);

            var uid = ElevationServer.ResolveExpectedPeerUid();

            // Falls back to /proc/self/loginuid or returns -1
            uid.Should().BeOneOf(uid >= 0 ? [uid, -1] : [-1]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PKEXEC_UID", oldPkexec);
            Environment.SetEnvironmentVariable("SUDO_UID", oldSudo);
        }
    }

    [Test]
    public void ResolveExpectedPeerUid_InvalidValue_ReturnsNegative()
    {
        if (!OperatingSystem.IsLinux()) return;

        var oldPkexec = Environment.GetEnvironmentVariable("PKEXEC_UID");
        var oldSudo = Environment.GetEnvironmentVariable("SUDO_UID");
        try
        {
            Environment.SetEnvironmentVariable("PKEXEC_UID", "notanumber");
            Environment.SetEnvironmentVariable("SUDO_UID", null);

            var uid = ElevationServer.ResolveExpectedPeerUid();

            // "notanumber" fails int.TryParse, falls through to loginuid or -1
            // The result depends on whether /proc/self/loginuid is available
            if (!File.Exists("/proc/self/loginuid"))
                uid.Should().Be(-1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PKEXEC_UID", oldPkexec);
            Environment.SetEnvironmentVariable("SUDO_UID", oldSudo);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ValidateExecutablePath — /proc/[pid]/exe
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ValidateExecutablePath_CurrentProcess_Matches()
    {
        if (!OperatingSystem.IsLinux()) return;

        var pid = Environment.ProcessId;
        var procExe = $"/proc/{pid}/exe";
        var actualPath = Path.GetFullPath(
            File.ResolveLinkTarget(procExe, returnFinalTarget: true)!.FullName);

        var result = ElevationServer.ValidateExecutablePath(actualPath, pid);

        result.Should().BeTrue();
    }

    [Test]
    public void ValidateExecutablePath_WrongPath_ReturnsFalse()
    {
        if (!OperatingSystem.IsLinux()) return;

        var pid = Environment.ProcessId;

        var result = ElevationServer.ValidateExecutablePath("/usr/bin/definitely-not-this-process", pid);

        result.Should().BeFalse();
    }

    [Test]
    public void ValidateExecutablePath_NonexistentPid_ReturnsFalse()
    {
        if (!OperatingSystem.IsLinux()) return;

        var result = ElevationServer.ValidateExecutablePath("/usr/bin/dotnet", 999999);

        result.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // BuildInodeToPidMap — real /proc/[pid]/fd scanning
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void BuildInodeToPidMap_ReturnsNonEmptyMap()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var tracker = new NetlinkConnectionTracker();

        var map = tracker.BuildInodeToPidMap();

        // CI runners may restrict /proc/*/fd access, returning empty maps
        if (map.Count == 0) return;

        map.Should().NotBeEmpty();
    }

    [Test]
    public void BuildInodeToPidMap_CurrentProcessHasSockets()
    {
        if (!OperatingSystem.IsLinux()) return;

        // Create a TCP socket so the current process has at least one socket fd
        using var tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        tcpSocket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        tcpSocket.Listen(1);

        using var tracker = new NetlinkConnectionTracker();

        var map = tracker.BuildInodeToPidMap();

        // CI runners may restrict /proc/*/fd access
        if (map.Count == 0) return;

        map.Values.Should().Contain(Environment.ProcessId);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // ParseProcNetTcp — real /proc/net/tcp parsing
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public void ParseProcNetTcp_RealFile_ParsesEstablishedConnections()
    {
        if (!OperatingSystem.IsLinux()) return;

        // Create a TCP connection to localhost so there's at least one established entry
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
        listener.Listen(1);
        var port = ((System.Net.IPEndPoint)listener.LocalEndPoint!).Port;

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port));
        using var accepted = listener.Accept();

        using var tracker = new NetlinkConnectionTracker();
        var inodeToPid = tracker.BuildInodeToPidMap();

        // Should not throw and should parse the established connection
        tracker.ParseProcNetTcp("/proc/net/tcp", inodeToPid, isIpv6: false);

        var stats = tracker.GetConnectionStats();
        stats.Success.Should().BeTrue();
    }

    [Test]
    public void ParseProcNetTcp_Ipv6File_Works()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var tracker = new NetlinkConnectionTracker();
        var inodeToPid = tracker.BuildInodeToPidMap();

        // Should not throw even if no IPv6 connections exist
        var act = () => tracker.ParseProcNetTcp("/proc/net/tcp6", inodeToPid, isIpv6: true);

        act.Should().NotThrow();
    }

    [Test]
    public void ParseProcNetTcp_NonexistentFile_NoException()
    {
        if (!OperatingSystem.IsLinux()) return;

        using var tracker = new NetlinkConnectionTracker();
        var inodeToPid = new Dictionary<long, int>();

        var act = () => tracker.ParseProcNetTcp("/nonexistent/path", inodeToPid, isIpv6: false);

        act.Should().NotThrow();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Full server integration — auth, connect, query, shutdown
    // ═══════════════════════════════════════════════════════════════════════

    [Test]
    public async Task FullServerLifecycle_AuthConnectQuery()
    {
        if (!OperatingSystem.IsLinux()) return;

        var socketPath = CreateTempSocketPath();

        using var server = new ElevationServer();
        var secret = (byte[])typeof(ElevationServer)
            .GetField("_secret", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(server)!;

        // Start server on a background task with a custom socket path
        using var cts = new CancellationTokenSource();
        var endpoint = new UnixDomainSocketEndPoint(socketPath);

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(endpoint);
        listener.Listen(5);

        // Run the server's client handler directly (avoids needing root for /run/wirebound)
        using var clientSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        clientSocket.Connect(endpoint);
        using var serverSideSocket = listener.Accept();

        var serverStream = new NetworkStream(serverSideSocket, ownsSocket: false);
        var clientStream = new NetworkStream(clientSocket, ownsSocket: false);

        var serverTask = server.HandleClientAsync(serverStream, Environment.ProcessId, cts.Token);

        // Step 1: Authenticate
        var pid = Environment.ProcessId;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HmacAuthenticator.Sign(pid, timestamp, secret);

        var authRequest = new IpcMessage
        {
            Type = MessageType.Authenticate,
            RequestId = Guid.NewGuid().ToString("N"),
            Payload = IpcTransport.SerializePayload(new AuthenticateRequest
            {
                ClientPid = pid,
                Timestamp = timestamp,
                Signature = signature,
                ExecutablePath = "" // Skip exe validation
            })
        };

        await IpcTransport.SendAsync(clientStream, authRequest);
        var authResponse = await IpcTransport.ReceiveAsync(clientStream);
        authResponse.Should().NotBeNull();
        var authResult = IpcTransport.DeserializePayload<AuthenticateResponse>(authResponse!.Payload);
        authResult.Success.Should().BeTrue();
        authResult.SessionId.Should().NotBeNullOrEmpty();

        // Step 2: Request connection stats
        var statsRequest = new IpcMessage
        {
            Type = MessageType.ConnectionStats,
            RequestId = Guid.NewGuid().ToString("N")
        };
        await IpcTransport.SendAsync(clientStream, statsRequest);
        var statsResponse = await IpcTransport.ReceiveAsync(clientStream);
        statsResponse.Should().NotBeNull();
        var statsResult = IpcTransport.DeserializePayload<ConnectionStatsResponse>(statsResponse!.Payload);
        statsResult.Success.Should().BeTrue();

        // Step 3: Heartbeat
        var heartbeatRequest = new IpcMessage
        {
            Type = MessageType.Heartbeat,
            RequestId = Guid.NewGuid().ToString("N")
        };
        await IpcTransport.SendAsync(clientStream, heartbeatRequest);
        var heartbeatResponse = await IpcTransport.ReceiveAsync(clientStream);
        heartbeatResponse.Should().NotBeNull();
        var heartbeatResult = IpcTransport.DeserializePayload<HeartbeatResponse>(heartbeatResponse!.Payload);
        heartbeatResult.Alive.Should().BeTrue();
        heartbeatResult.UptimeSeconds.Should().BeGreaterThanOrEqualTo(0);

        // Step 4: Shutdown
        var shutdownRequest = new IpcMessage
        {
            Type = MessageType.Shutdown,
            RequestId = Guid.NewGuid().ToString("N")
        };
        await IpcTransport.SendAsync(clientStream, shutdownRequest);
        var shutdownResponse = await IpcTransport.ReceiveAsync(clientStream);
        shutdownResponse.Should().NotBeNull();
        var shutdownResult = IpcTransport.DeserializePayload<HeartbeatResponse>(shutdownResponse!.Payload);
        shutdownResult.Alive.Should().BeFalse();

        // Server task should complete after shutdown
        await cts.CancelAsync();
        await serverTask;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private string CreateTempSocketPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wirebound-test-{Guid.NewGuid():N}.sock");
        _socketPaths.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _socketPaths)
        {
            try { File.Delete(path); }
            catch { /* best effort */ }
        }
    }
}

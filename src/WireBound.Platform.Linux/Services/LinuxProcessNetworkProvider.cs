using System.Net;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;

namespace WireBound.Platform.Linux.Services;

/// <summary>
/// Linux implementation of IProcessNetworkProvider using /proc filesystem.
/// Parses /proc/net/tcp, /proc/net/udp and correlates with /proc/[pid]/fd
/// to map connections to processes.
/// Non-elevated mode: limited visibility (own user's processes only).
/// Elevated mode (root): full visibility of all processes.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed partial class LinuxProcessNetworkProvider : IProcessNetworkProvider
{
    private readonly Dictionary<int, ProcessNetworkStats> _processStats = [];
    private readonly Dictionary<int, ProcessCacheEntry> _processCache = [];
    private readonly object _lock = new();

    private bool _isMonitoring;
    private CancellationTokenSource? _monitoringCts;
    private Task? _monitoringTask;
    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

    public ProcessNetworkCapabilities Capabilities =>
        ProcessNetworkCapabilities.ConnectionList |
        ProcessNetworkCapabilities.RequiresElevation; // Full visibility needs root

    public bool IsMonitoring => _isMonitoring;

    public event EventHandler<ProcessNetworkProviderEventArgs>? StatsUpdated;
    public event EventHandler<ProcessNetworkProviderErrorEventArgs>? ErrorOccurred;

    public Task<bool> StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (_isMonitoring)
            return Task.FromResult(true);

        try
        {
            _isMonitoring = true;
            _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _monitoringTask = MonitorAsync(_monitoringCts.Token);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _isMonitoring = false;
            ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(
                $"Failed to start monitoring: {ex.Message}", ex, false));
            return Task.FromResult(false);
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, cancellationToken);
                UpdateStats();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(
                    $"Monitoring error: {ex.Message}", ex, true));
            }
        }
    }

    private void UpdateStats()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.Now;
            var interval = now - _lastUpdate;
            _lastUpdate = now;

            // Build inode to PID mapping from /proc/[pid]/fd
            var inodeToPid = BuildInodeToPidMap();

            // Parse /proc/net/* files
            var connections = new List<InternalConnectionInfo>();
            connections.AddRange(ParseProcNetFile("/proc/net/tcp", "TCP", inodeToPid));
            connections.AddRange(ParseProcNetFile("/proc/net/tcp6", "TCP6", inodeToPid));
            connections.AddRange(ParseProcNetFile("/proc/net/udp", "UDP", inodeToPid));
            connections.AddRange(ParseProcNetFile("/proc/net/udp6", "UDP6", inodeToPid));

            // Group by process ID
            var connectionsByProcess = connections
                .Where(c => c.ProcessId > 0)
                .GroupBy(c => c.ProcessId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Update stats
            foreach (var (pid, processConnections) in connectionsByProcess)
            {
                if (!_processStats.TryGetValue(pid, out var stats))
                {
                    var processInfo = GetProcessInfo(pid);
                    stats = new ProcessNetworkStats
                    {
                        ProcessId = pid,
                        ProcessName = processInfo.Name,
                        DisplayName = processInfo.DisplayName,
                        ExecutablePath = processInfo.Path,
                        AppIdentifier = ComputeAppIdentifier(processInfo.Path),
                        FirstSeen = DateTime.Now
                    };
                    _processStats[pid] = stats;
                }

                stats.LastSeen = DateTime.Now;
            }

            // Mark inactive processes
            var activePids = connectionsByProcess.Keys.ToHashSet();
            foreach (var (pid, stats) in _processStats)
            {
                if (!activePids.Contains(pid))
                {
                    stats.DownloadSpeedBps = 0;
                    stats.UploadSpeedBps = 0;
                }
            }

            var statsList = _processStats.Values.ToList().AsReadOnly();
            StatsUpdated?.Invoke(this, new ProcessNetworkProviderEventArgs(statsList, now, interval));
        }
    }

    public Task<IReadOnlyList<ConnectionInfo>> GetActiveConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var inodeToPid = BuildInodeToPidMap();
        var connections = new List<ConnectionInfo>();

        foreach (var conn in ParseProcNetFile("/proc/net/tcp", "TCP", inodeToPid))
        {
            connections.Add(ToConnectionInfo(conn));
        }
        foreach (var conn in ParseProcNetFile("/proc/net/tcp6", "TCP", inodeToPid))
        {
            connections.Add(ToConnectionInfo(conn));
        }
        foreach (var conn in ParseProcNetFile("/proc/net/udp", "UDP", inodeToPid))
        {
            connections.Add(ToConnectionInfo(conn));
        }
        foreach (var conn in ParseProcNetFile("/proc/net/udp6", "UDP", inodeToPid))
        {
            connections.Add(ToConnectionInfo(conn));
        }

        return Task.FromResult<IReadOnlyList<ConnectionInfo>>(connections.AsReadOnly());
    }

    public Task<IReadOnlyList<ConnectionStats>> GetConnectionStatsAsync(CancellationToken cancellationToken = default)
    {
        var inodeToPid = BuildInodeToPidMap();
        var stats = new List<ConnectionStats>();

        var allConnections = new List<InternalConnectionInfo>();
        allConnections.AddRange(ParseProcNetFile("/proc/net/tcp", "TCP", inodeToPid));
        allConnections.AddRange(ParseProcNetFile("/proc/net/tcp6", "TCP", inodeToPid));
        allConnections.AddRange(ParseProcNetFile("/proc/net/udp", "UDP", inodeToPid));
        allConnections.AddRange(ParseProcNetFile("/proc/net/udp6", "UDP", inodeToPid));

        foreach (var conn in allConnections)
        {
            var processInfo = GetProcessInfo(conn.ProcessId);
            stats.Add(new ConnectionStats
            {
                LocalAddress = conn.LocalAddress?.ToString() ?? "",
                LocalPort = conn.LocalPort,
                RemoteAddress = conn.RemoteAddress?.ToString() ?? "",
                RemotePort = conn.RemotePort,
                ProcessId = conn.ProcessId,
                ProcessName = processInfo.Name,
                Protocol = conn.Protocol,
                State = conn.State,
                BytesSent = 0,      // Not available without eBPF
                BytesReceived = 0   // Not available without eBPF
            });
        }

        return Task.FromResult<IReadOnlyList<ConnectionStats>>(stats.AsReadOnly());
    }

    private static ConnectionInfo ToConnectionInfo(InternalConnectionInfo conn)
    {
        return new ConnectionInfo
        {
            ProcessId = conn.ProcessId,
            Protocol = conn.Protocol,
            LocalAddress = conn.LocalAddress?.ToString() ?? "",
            LocalPort = conn.LocalPort,
            RemoteAddress = conn.RemoteAddress?.ToString() ?? "",
            RemotePort = conn.RemotePort,
            State = conn.State
        };
    }

    private Dictionary<long, int> BuildInodeToPidMap()
    {
        var map = new Dictionary<long, int>();

        try
        {
            var procDir = new DirectoryInfo("/proc");
            foreach (var pidDir in procDir.EnumerateDirectories())
            {
                if (!int.TryParse(pidDir.Name, out int pid))
                    continue;

                var fdDir = Path.Combine(pidDir.FullName, "fd");
                if (!Directory.Exists(fdDir))
                    continue;

                try
                {
                    foreach (var fd in Directory.EnumerateFiles(fdDir))
                    {
                        try
                        {
                            var link = File.ResolveLinkTarget(fd, false);
                            if (link == null) continue;

                            var target = link.FullName;
                            var match = SocketInodeRegex().Match(target);
                            if (match.Success && long.TryParse(match.Groups[1].Value, out long inode))
                            {
                                map[inode] = pid;
                            }
                        }
                        catch (IOException)
                        {
                            // FD no longer exists or permission denied
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Can't read this process's fds
                }
                catch (DirectoryNotFoundException)
                {
                    // Process exited
                }
            }
        }
        catch (Exception)
        {
            // /proc access failed
        }

        return map;
    }

    private List<InternalConnectionInfo> ParseProcNetFile(string path, string protocol, Dictionary<long, int> inodeToPid)
    {
        var connections = new List<InternalConnectionInfo>();

        if (!File.Exists(path))
            return connections;

        try
        {
            var lines = File.ReadAllLines(path);
            foreach (var line in lines.Skip(1)) // Skip header
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 10)
                    continue;

                try
                {
                    var localParts = parts[1].Split(':');
                    var remoteParts = parts[2].Split(':');

                    if (long.TryParse(parts[9], out long inode) && inodeToPid.TryGetValue(inode, out int pid))
                    {
                        var stateHex = parts[3];
                        connections.Add(new InternalConnectionInfo
                        {
                            ProcessId = pid,
                            Protocol = protocol,
                            LocalAddress = ParseHexIp(localParts[0]),
                            LocalPort = Convert.ToInt32(localParts[1], 16),
                            RemoteAddress = ParseHexIp(remoteParts[0]),
                            RemotePort = Convert.ToInt32(remoteParts[1], 16),
                            Inode = inode,
                            State = MapLinuxTcpState(stateHex)
                        });
                    }
                }
                catch (Exception)
                {
                    // Skip malformed lines
                }
            }
        }
        catch (Exception)
        {
            // File read failed
        }

        return connections;
    }

    private static ConnectionState MapLinuxTcpState(string hexState)
    {
        return hexState.ToUpperInvariant() switch
        {
            "01" => ConnectionState.Established,
            "02" => ConnectionState.SynSent,
            "03" => ConnectionState.SynReceived,
            "04" => ConnectionState.FinWait1,
            "05" => ConnectionState.FinWait2,
            "06" => ConnectionState.TimeWait,
            "07" => ConnectionState.Closed,
            "08" => ConnectionState.CloseWait,
            "09" => ConnectionState.LastAck,
            "0A" => ConnectionState.Listen,
            "0B" => ConnectionState.Closing,
            _ => ConnectionState.Unknown
        };
    }

    private static IPAddress? ParseHexIp(string hex)
    {
        if (hex.Length == 8) // IPv4
        {
            var bytes = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                bytes[3 - i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return new IPAddress(bytes);
        }
        else if (hex.Length == 32) // IPv6
        {
            var bytes = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return new IPAddress(bytes);
        }
        return null;
    }

    private ProcessCacheEntry GetProcessInfo(int pid)
    {
        if (_processCache.TryGetValue(pid, out var cached))
            return cached;

        var info = new ProcessCacheEntry { Name = $"pid-{pid}", DisplayName = $"Unknown ({pid})", Path = "" };

        try
        {
            var commPath = $"/proc/{pid}/comm";
            if (File.Exists(commPath))
            {
                info.Name = File.ReadAllText(commPath).Trim();
                info.DisplayName = info.Name;
            }

            var exePath = $"/proc/{pid}/exe";
            if (File.Exists(exePath))
            {
                try
                {
                    var target = File.ResolveLinkTarget(exePath, false);
                    if (target != null)
                    {
                        info.Path = target.FullName;
                    }
                }
                catch (IOException)
                {
                    // Permission denied or deleted
                }
            }
        }
        catch (Exception)
        {
            // Process may have exited
        }

        _processCache[pid] = info;
        return info;
    }

    private static string ComputeAppIdentifier(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "unknown";

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    public async Task StopMonitoringAsync()
    {
        if (!_isMonitoring)
            return;

        _isMonitoring = false;

        if (_monitoringCts != null)
        {
            await _monitoringCts.CancelAsync();

            if (_monitoringTask != null)
            {
                try { await _monitoringTask; }
                catch (OperationCanceledException) { }
            }

            _monitoringCts.Dispose();
            _monitoringCts = null;
        }
    }

    public Task<IReadOnlyList<ProcessNetworkStats>> GetProcessStatsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ProcessNetworkStats>>(_processStats.Values.ToList().AsReadOnly());
        }
    }

    public void Dispose()
    {
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
    }

    [GeneratedRegex(@"socket:\[(\d+)\]")]
    private static partial Regex SocketInodeRegex();

    #region Helper Types

    private class InternalConnectionInfo
    {
        public int ProcessId { get; set; }
        public string Protocol { get; set; } = "";
        public IPAddress? LocalAddress { get; set; }
        public int LocalPort { get; set; }
        public IPAddress? RemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public long Inode { get; set; }
        public ConnectionState State { get; set; } = ConnectionState.Unknown;
    }

    private class ProcessCacheEntry
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Path { get; set; } = "";
    }

    #endregion
}

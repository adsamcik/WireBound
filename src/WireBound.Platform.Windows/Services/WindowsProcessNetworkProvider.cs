using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using WireBound.Platform.Abstract.Models;
using WireBound.Platform.Abstract.Services;
using AbstractConnectionInfo = WireBound.Platform.Abstract.Models.ConnectionInfo;

namespace WireBound.Platform.Windows.Services;

/// <summary>
/// Windows implementation of IProcessNetworkProvider using IP Helper API.
/// Uses GetExtendedTcpTable/GetExtendedUdpTable for connection-to-process mapping.
/// Non-elevated mode: can enumerate connections but not track bytes per connection.
/// Elevated mode: could use ETW for full byte tracking (future enhancement).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessNetworkProvider : IProcessNetworkProvider
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
        ProcessNetworkCapabilities.RequiresElevation; // Full byte tracking needs elevation

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

            // Get all TCP and UDP connections with their owning process IDs
            var connections = GetAllConnectionsInternal();

            // Group connections by process ID
            var connectionsByProcess = connections
                .GroupBy(c => c.ProcessId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Update or create stats for each process
            foreach (var (pid, processConnections) in connectionsByProcess)
            {
                if (pid == 0) continue; // Skip system idle process

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

            // Raise update event
            var statsList = _processStats.Values.ToList().AsReadOnly();
            StatsUpdated?.Invoke(this, new ProcessNetworkProviderEventArgs(statsList, now, interval));
        }
    }

    public Task<IReadOnlyList<AbstractConnectionInfo>> GetActiveConnectionsAsync(CancellationToken cancellationToken = default)
    {
        var connections = new List<AbstractConnectionInfo>();

        try
        {
            connections.AddRange(GetTcpConnections());
            connections.AddRange(GetUdpConnections());
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(
                $"Failed to get connections: {ex.Message}", ex, true));
        }

        return Task.FromResult<IReadOnlyList<AbstractConnectionInfo>>(connections.AsReadOnly());
    }

    public Task<IReadOnlyList<ConnectionStats>> GetConnectionStatsAsync(CancellationToken cancellationToken = default)
    {
        // Without elevation, we can only get connection info, not byte stats
        // Convert ConnectionInfo to ConnectionStats with zero byte counts
        var connections = GetAllConnectionsInternal();
        var stats = connections.Select(c =>
        {
            var processInfo = GetProcessInfo(c.ProcessId);
            return new ConnectionStats
            {
                LocalAddress = c.LocalAddress?.ToString() ?? "",
                LocalPort = c.LocalPort,
                RemoteAddress = c.RemoteAddress?.ToString() ?? "",
                RemotePort = c.RemotePort,
                ProcessId = c.ProcessId,
                ProcessName = processInfo.Name,
                Protocol = c.Protocol,
                State = c.State,
                BytesSent = 0,      // Not available without elevation
                BytesReceived = 0   // Not available without elevation
            };
        }).ToList();

        return Task.FromResult<IReadOnlyList<ConnectionStats>>(stats.AsReadOnly());
    }

    private List<InternalConnectionInfo> GetAllConnectionsInternal()
    {
        var connections = new List<InternalConnectionInfo>();

        try
        {
            connections.AddRange(GetTcpConnectionsInternal());
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(
                $"Failed to get TCP connections: {ex.Message}", ex, true));
        }

        try
        {
            connections.AddRange(GetUdpConnectionsInternal());
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, new ProcessNetworkProviderErrorEventArgs(
                $"Failed to get UDP connections: {ex.Message}", ex, true));
        }

        return connections;
    }

    private List<AbstractConnectionInfo> GetTcpConnections()
    {
        return GetTcpConnectionsInternal().Select(c => new AbstractConnectionInfo
        {
            ProcessId = c.ProcessId,
            Protocol = c.Protocol,
            LocalAddress = c.LocalAddress?.ToString() ?? "",
            LocalPort = c.LocalPort,
            RemoteAddress = c.RemoteAddress?.ToString() ?? "",
            RemotePort = c.RemotePort,
            State = c.State
        }).ToList();
    }

    private List<AbstractConnectionInfo> GetUdpConnections()
    {
        return GetUdpConnectionsInternal().Select(c => new AbstractConnectionInfo
        {
            ProcessId = c.ProcessId,
            Protocol = c.Protocol,
            LocalAddress = c.LocalAddress?.ToString() ?? "",
            LocalPort = c.LocalPort,
            RemoteAddress = "",
            RemotePort = 0,
            State = ConnectionState.Unknown
        }).ToList();
    }

    private List<InternalConnectionInfo> GetTcpConnectionsInternal()
    {
        var connections = new List<InternalConnectionInfo>();

        int bufferSize = 0;
        int result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

        if (result != ERROR_INSUFFICIENT_BUFFER && result != 0)
            return connections;

        IntPtr tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
            if (result != 0)
                return connections;

            int numEntries = Marshal.ReadInt32(tcpTablePtr);
            IntPtr rowPtr = tcpTablePtr + 4;
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                connections.Add(new InternalConnectionInfo
                {
                    ProcessId = (int)row.owningPid,
                    Protocol = "TCP",
                    LocalAddress = new IPAddress(row.localAddr),
                    LocalPort = (ushort)IPAddress.NetworkToHostOrder((short)row.localPort),
                    RemoteAddress = new IPAddress(row.remoteAddr),
                    RemotePort = (ushort)IPAddress.NetworkToHostOrder((short)row.remotePort),
                    State = MapTcpState(row.state)
                });
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }

        return connections;
    }

    private List<InternalConnectionInfo> GetUdpConnectionsInternal()
    {
        var connections = new List<InternalConnectionInfo>();

        int bufferSize = 0;
        int result = GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);

        if (result != ERROR_INSUFFICIENT_BUFFER && result != 0)
            return connections;

        IntPtr udpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            result = GetExtendedUdpTable(udpTablePtr, ref bufferSize, false, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
            if (result != 0)
                return connections;

            int numEntries = Marshal.ReadInt32(udpTablePtr);
            IntPtr rowPtr = udpTablePtr + 4;
            int rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();

            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                connections.Add(new InternalConnectionInfo
                {
                    ProcessId = (int)row.owningPid,
                    Protocol = "UDP",
                    LocalAddress = new IPAddress(row.localAddr),
                    LocalPort = (ushort)IPAddress.NetworkToHostOrder((short)row.localPort)
                });
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(udpTablePtr);
        }

        return connections;
    }

    private static ConnectionState MapTcpState(uint state)
    {
        return state switch
        {
            1 => ConnectionState.Closed,
            2 => ConnectionState.Listen,
            3 => ConnectionState.SynSent,
            4 => ConnectionState.SynReceived,
            5 => ConnectionState.Established,
            6 => ConnectionState.FinWait1,
            7 => ConnectionState.FinWait2,
            8 => ConnectionState.CloseWait,
            9 => ConnectionState.Closing,
            10 => ConnectionState.LastAck,
            11 => ConnectionState.TimeWait,
            12 => ConnectionState.DeleteTcb,
            _ => ConnectionState.Unknown
        };
    }

    private ProcessCacheEntry GetProcessInfo(int pid)
    {
        if (_processCache.TryGetValue(pid, out var cached))
            return cached;

        var info = new ProcessCacheEntry { Name = $"PID {pid}", DisplayName = $"Unknown ({pid})", Path = "" };

        try
        {
            using var process = Process.GetProcessById(pid);
            info.Name = process.ProcessName;
            info.DisplayName = process.ProcessName;

            try
            {
                info.Path = process.MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(info.Path))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(info.Path);
                    if (!string.IsNullOrEmpty(versionInfo.FileDescription))
                    {
                        info.DisplayName = versionInfo.FileDescription;
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Access denied - can't get path for elevated processes
            }
        }
        catch (ArgumentException)
        {
            // Process no longer exists
        }
        catch (InvalidOperationException)
        {
            // Process has exited
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

    #region P/Invoke Definitions

    private const int AF_INET = 2;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    private enum TCP_TABLE_CLASS
    {
        TCP_TABLE_BASIC_LISTENER,
        TCP_TABLE_BASIC_CONNECTIONS,
        TCP_TABLE_BASIC_ALL,
        TCP_TABLE_OWNER_PID_LISTENER,
        TCP_TABLE_OWNER_PID_CONNECTIONS,
        TCP_TABLE_OWNER_PID_ALL,
        TCP_TABLE_OWNER_MODULE_LISTENER,
        TCP_TABLE_OWNER_MODULE_CONNECTIONS,
        TCP_TABLE_OWNER_MODULE_ALL
    }

    private enum UDP_TABLE_CLASS
    {
        UDP_TABLE_BASIC,
        UDP_TABLE_OWNER_PID,
        UDP_TABLE_OWNER_MODULE
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state;
        public uint localAddr;
        public uint localPort;
        public uint remoteAddr;
        public uint remotePort;
        public uint owningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint localAddr;
        public uint localPort;
        public uint owningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        TCP_TABLE_CLASS tblClass,
        int reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        UDP_TABLE_CLASS tblClass,
        int reserved);

    #endregion

    #region Helper Types

    private class InternalConnectionInfo
    {
        public int ProcessId { get; set; }
        public string Protocol { get; set; } = "";
        public IPAddress? LocalAddress { get; set; }
        public int LocalPort { get; set; }
        public IPAddress? RemoteAddress { get; set; }
        public int RemotePort { get; set; }
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

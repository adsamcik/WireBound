using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Serilog;
using WireBound.IPC.Messages;

namespace WireBound.Elevation.Windows;

/// <summary>
/// Tracks per-connection byte statistics using ETW (Event Tracing for Windows).
/// Subscribes to the Microsoft-Windows-TCPIP provider for real-time TCP byte counters
/// and uses GetExtendedTcpTable for PID-to-connection mapping.
/// </summary>
public sealed class EtwConnectionTracker : IDisposable
{
    private const string SessionName = "WireBound-TCPIP-Session";

    // ETW event IDs for TCP data transfer
    private const int TcpDataTransferReceive = 1201;
    private const int TcpDataTransferSend = 1202;

    private TraceEventSession? _etwSession;
    private Thread? _processingThread;
    private volatile bool _running;

    // Connection key: "localIP:localPort-remoteIP:remotePort"
    private readonly ConcurrentDictionary<string, ConnectionBytes> _connectionBytes = new();

    // PID to connection mapping, refreshed periodically
    private readonly ConcurrentDictionary<string, int> _connectionToPid = new();
    private readonly ConcurrentDictionary<int, string> _pidToProcessName = new();
    private readonly Timer _refreshTimer;

    public EtwConnectionTracker()
    {
        _refreshTimer = new Timer(_ => RefreshConnectionPidMapping(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        // Start PID mapping refresh
        _refreshTimer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(2));

        // Start ETW session in background thread (blocking call)
        _processingThread = new Thread(RunEtwSession)
        {
            IsBackground = true,
            Name = "ETW-TCPIP-Processor"
        };
        _processingThread.Start();
    }

    public void Stop()
    {
        _running = false;
        _refreshTimer.Change(Timeout.Infinite, Timeout.Infinite);

        try
        {
            _etwSession?.Stop();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error stopping ETW session");
        }
    }

    private void RunEtwSession()
    {
        try
        {
            // Clean up any stale session from a previous crash
            try
            {
                using var stale = TraceEventSession.GetActiveSession(SessionName);
                stale?.Stop();
            }
            catch { /* ignore */ }

            using var session = new TraceEventSession(SessionName);
            _etwSession = session;

            // Subscribe to Microsoft-Windows-TCPIP provider
            // Keywords 0x40 = TCPIP data transfer events
            session.EnableProvider("Microsoft-Windows-TCPIP", TraceEventLevel.Informational, 0x40);

            session.Source.AllEvents += OnEtwEvent;

            Log.Information("ETW session started, processing TCP events");
            session.Source.Process(); // Blocks until session stops
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Error(ex, "ETW session requires administrator privileges");
        }
        catch (Exception ex) when (_running)
        {
            Log.Error(ex, "ETW session error");
        }
    }

    private void OnEtwEvent(TraceEvent data)
    {
        try
        {
            var eventId = (int)data.ID;

            if (eventId is not (TcpDataTransferReceive or TcpDataTransferSend))
                return;

            // Extract connection info from event payload
            var connId = data.PayloadByName("Tcb")?.ToString();
            if (string.IsNullOrEmpty(connId)) return;

            var size = 0;
            var sizeObj = data.PayloadByName("NumBytes");
            if (sizeObj is int intSize)
                size = intSize;
            else if (sizeObj is uint uintSize)
                size = (int)uintSize;

            if (size <= 0) return;

            var entry = _connectionBytes.GetOrAdd(connId, _ => new ConnectionBytes());

            if (eventId == TcpDataTransferReceive)
                Interlocked.Add(ref entry.BytesReceived, size);
            else
                Interlocked.Add(ref entry.BytesSent, size);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error processing ETW event {EventId}", data.ID);
        }
    }

    /// <summary>
    /// Refreshes the mapping of connections to PIDs using GetExtendedTcpTable P/Invoke.
    /// Uses a retry loop to handle ERROR_INSUFFICIENT_BUFFER (122) when the TCP table
    /// grows between the size query and the data fetch.
    /// </summary>
    private void RefreshConnectionPidMapping()
    {
        try
        {
            var size = 0;
            // First call to get required buffer size
            GetExtendedTcpTable(IntPtr.Zero, ref size, order: true,
                AF_INET, TcpTableClass.TcpTableOwnerPidAll, 0);

            // Retry loop: the TCP table can grow between size query and data fetch
            const int maxRetries = 3;
            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                // Add 10% padding to reduce retry likelihood
                var allocSize = size + (size / 10);
                var buffer = Marshal.AllocHGlobal(allocSize);
                try
                {
                    var result = GetExtendedTcpTable(buffer, ref allocSize, order: true,
                        AF_INET, TcpTableClass.TcpTableOwnerPidAll, 0);

                    if (result == ErrorInsufficientBuffer)
                    {
                        size = allocSize; // Use the updated size for next attempt
                        Log.Debug("GetExtendedTcpTable buffer too small, retrying (attempt {Attempt})", attempt + 1);
                        continue;
                    }

                    if (result != 0)
                    {
                        Log.Debug("GetExtendedTcpTable failed with error code {ErrorCode}", result);
                        return;
                    }

                    var rowCount = Marshal.ReadInt32(buffer);
                    var rowPtr = buffer + Marshal.SizeOf<int>();
                    var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

                    // Validate rowCount against allocated buffer to prevent overread
                    var maxRows = (allocSize - Marshal.SizeOf<int>()) / rowSize;
                    if (rowCount < 0 || rowCount > maxRows)
                    {
                        Log.Warning("GetExtendedTcpTable returned invalid row count {RowCount} (max {MaxRows})",
                            rowCount, maxRows);
                        return;
                    }

                    for (var i = 0; i < rowCount; i++)
                    {
                        var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr + i * rowSize);

                        var localAddr = new IPAddress(row.LocalAddr).ToString();
                        var localPort = (row.LocalPort >> 8) | ((row.LocalPort & 0xFF) << 8);
                        var remoteAddr = new IPAddress(row.RemoteAddr).ToString();
                        var remotePort = (row.RemotePort >> 8) | ((row.RemotePort & 0xFF) << 8);

                        var key = MakeConnectionKey(localAddr, localPort, remoteAddr, remotePort);
                        _connectionToPid[key] = row.OwningPid;

                        if (row.OwningPid != 0 && !_pidToProcessName.ContainsKey(row.OwningPid))
                        {
                            try
                            {
                                using var proc = System.Diagnostics.Process.GetProcessById(row.OwningPid);
                                _pidToProcessName[row.OwningPid] = proc.ProcessName;
                            }
                            catch
                            {
                                _pidToProcessName[row.OwningPid] = "Unknown";
                            }
                        }
                    }

                    return; // Success — exit retry loop
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            Log.Warning("GetExtendedTcpTable failed after {MaxRetries} retries due to buffer growth", maxRetries);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error refreshing connection-PID mapping");
        }
    }

    #region P/Invoke for GetExtendedTcpTable

    private const int AF_INET = 2;
    private const int ErrorInsufficientBuffer = 122;

    private enum TcpTableClass
    {
        TcpTableOwnerPidAll = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public int LocalPort;
        public uint RemoteAddr;
        public int RemotePort;
        public int OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        [MarshalAs(UnmanagedType.Bool)] bool order,
        int ulAf,
        TcpTableClass tableClass,
        int reserved);

    #endregion

    public ConnectionStatsResponse GetConnectionStats()
    {
        try
        {
            var processes = new Dictionary<int, ProcessConnectionStats>();
            var properties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
            var connections = properties.GetActiveTcpConnections();

            // Build connection stats from ETW byte data
            foreach (var entry in _connectionBytes)
            {
                var bytes = entry.Value;
                if (bytes.BytesSent == 0 && bytes.BytesReceived == 0)
                    continue;

                var pid = _connectionToPid.GetValueOrDefault(entry.Key, 0);
                if (!processes.TryGetValue(pid, out var processStats))
                {
                    processStats = new ProcessConnectionStats
                    {
                        ProcessId = pid,
                        ProcessName = _pidToProcessName.GetValueOrDefault(pid, "Unknown")
                    };
                    processes[pid] = processStats;
                }

                processStats.BytesSent += Interlocked.Read(ref bytes.BytesSent);
                processStats.BytesReceived += Interlocked.Read(ref bytes.BytesReceived);
            }

            // Add active connections without ETW data
            foreach (var conn in connections.Take(200))
            {
                var key = MakeConnectionKey(
                    conn.LocalEndPoint.Address.ToString(),
                    conn.LocalEndPoint.Port,
                    conn.RemoteEndPoint.Address.ToString(),
                    conn.RemoteEndPoint.Port);

                var bytes = _connectionBytes.GetValueOrDefault(key);
                var pid = _connectionToPid.GetValueOrDefault(key, 0);

                if (!processes.TryGetValue(pid, out var processStats))
                {
                    processStats = new ProcessConnectionStats
                    {
                        ProcessId = pid,
                        ProcessName = _pidToProcessName.GetValueOrDefault(pid, "Unknown")
                    };
                    processes[pid] = processStats;
                }

                processStats.Connections.Add(new ConnectionByteStats
                {
                    LocalAddress = conn.LocalEndPoint.Address.ToString(),
                    LocalPort = conn.LocalEndPoint.Port,
                    RemoteAddress = conn.RemoteEndPoint.Address.ToString(),
                    RemotePort = conn.RemoteEndPoint.Port,
                    Protocol = 6, // TCP
                    BytesSent = bytes is not null ? Interlocked.Read(ref bytes.BytesSent) : 0,
                    BytesReceived = bytes is not null ? Interlocked.Read(ref bytes.BytesReceived) : 0
                });
            }

            return new ConnectionStatsResponse
            {
                Success = true,
                Processes = processes.Values.ToList()
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get connection stats");
            return new ConnectionStatsResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public ProcessStatsResponse GetProcessStats(List<int> filterPids)
    {
        try
        {
            var connectionStats = GetConnectionStats();
            if (!connectionStats.Success)
            {
                return new ProcessStatsResponse
                {
                    Success = false,
                    ErrorMessage = connectionStats.ErrorMessage
                };
            }

            var processes = connectionStats.Processes
                .Where(p => filterPids.Count == 0 || filterPids.Contains(p.ProcessId))
                .Select(p => new ProcessByteStats
                {
                    ProcessId = p.ProcessId,
                    ProcessName = p.ProcessName,
                    TotalBytesSent = p.BytesSent,
                    TotalBytesReceived = p.BytesReceived,
                    ActiveConnectionCount = p.Connections.Count
                })
                .ToList();

            return new ProcessStatsResponse
            {
                Success = true,
                Processes = processes
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get process stats");
            return new ProcessStatsResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string MakeConnectionKey(string localAddr, int localPort, string remoteAddr, int remotePort) =>
        $"{localAddr}:{localPort}-{remoteAddr}:{remotePort}";

    public void Dispose()
    {
        Stop();

        // Wait for any in-flight timer callback to complete before disposing
        using var timerDone = new ManualResetEvent(false);
        if (!_refreshTimer.Dispose(timerDone))
            timerDone.WaitOne(TimeSpan.FromSeconds(5));

        _etwSession?.Dispose();
    }

    private class ConnectionBytes
    {
        public long BytesSent;
        public long BytesReceived;
    }
}

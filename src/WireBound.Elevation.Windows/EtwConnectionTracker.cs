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
    private const string SessionName = $"WireBound-TCPIP-{nameof(EtwConnectionTracker)}";
    private static string ProcessSessionName => $"{SessionName}-{Environment.ProcessId}";

    // ETW event IDs for the Microsoft-Windows-TCPIP user-mode manifest provider
    // on Windows 10/11 (provider GUID 2f07e2ee-15db-40f1-90ef-9d7ba282188a).
    // Discovered from the runtime manifest via Get-WinEvent -ListProvider; the
    // numbers used to be 1201/1202/1010/1011/1003/1013/1015 in pre-Win10 builds
    // but the modern provider uses these IDs and the prior constants matched
    // nothing at all on Windows 11 (the diagnostic counter showed millions of
    // raw events with only ~9/sec matching the old IDs).
    //
    // Data-transfer events — payload: { Tcb (HexInt64 pointer), NumBytes (UInt32), ... }
    private const int TcpDataTransferSend = 1073;
    private const int TcpDataTransferReceive = 1074;

    // Connection lifecycle events — payload: { LocalAddress, RemoteAddress, Status,
    // ProcessId (UInt32), Compartment, Tcb (HexInt64 pointer) }.
    // We use the Tcb itself as the connection identity key (it is unique for
    // the lifetime of the connection); the LocalAddress / RemoteAddress fields
    // are SocketAddress binary blobs and not worth parsing here.
    private const int TcpAcceptComplete = 1017;
    private const int TcpConnectComplete = 1033;
    private const int TcpCloseIssued = 1038;
    private const int TcpAbortIssued = 1039;
    private const int TcpAbortCompleted = 1040;
    private const int TcpDisconnectCompleted = 1043;

    private TraceEventSession? _etwSession;
    private Thread? _processingThread;
    private volatile bool _running;

    // Diagnostic counters — surfaced periodically through RefreshConnectionPidMapping
    // so we can see whether ETW is producing events at all, how many of them match
    // our data-transfer handler, and whether they translate into accumulated bytes.
    // Critical for diagnosing "0 B everywhere" symptoms where the session reports
    // started but the keyword mask or event IDs don't match the running OS build.
    private long _etwEventsReceived;
    private long _etwDataEventsMatched;
    private long _etwLifecycleEventsMatched;
    private long _etwBytesCaptured;
    private int _diagnosticTickCount;
    private const int DiagnosticsLogEveryNTicks = 5; // refresh runs every 2s → log every ~10s

    // Discovery mode: captures the first N unique event IDs the TCPIP provider
    // emits along with their canonical name + payload field names. Lets us see
    // exactly which constants to use when 1201/1202 etc. don't match the running
    // Windows build — without attaching a debugger or PerfView. Logged once per
    // unique ID then suppressed; the dictionary doubles as the "seen" set.
    private readonly ConcurrentDictionary<int, byte> _discoveredEventIds = new();
    private const int MaxDiscoveredEventTypes = 100;

    // Connection key: "localIP:localPort-remoteIP:remotePort"
    private readonly ConcurrentDictionary<string, ConnectionBytes> _connectionBytes = new();
    private const int MaxTrackedConnections = 10_000;
    private const int MaxTcbMappings = 50_000;

    // Tcb (kernel pointer) → connection key mapping, populated from connection lifecycle events
    private readonly ConcurrentDictionary<string, TcbMapping> _tcbToConnection = new();

    // PID to connection mapping, refreshed periodically
    private readonly ConcurrentDictionary<string, int> _connectionToPid = new();
    private readonly ConcurrentDictionary<int, ProcessIdentity> _pidToIdentity = new();
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
            // Clean up any stale session from a previous crash of THIS process
            try
            {
                using var stale = TraceEventSession.GetActiveSession(ProcessSessionName);
                stale?.Stop();
            }
            catch { /* ignore */ }

            using var session = new TraceEventSession(ProcessSessionName);
            _etwSession = session;

            // Enable Microsoft-Windows-TCPIP at Verbose with ALL keywords.
            // Earlier the mask was 0x41 (intended: data-transfer + connection
            // lifecycle), but the meaning of TCPIP provider keywords differs
            // by Windows build and that mask captured nothing on Windows 11 —
            // every per-process byte counter ended up zero. Enabling all
            // keywords adds modest overhead but guarantees the events we care
            // about (data-transfer 1201/1202, lifecycle 1003/1010/1011/1013/1015)
            // are delivered.
            session.EnableProvider(
                "Microsoft-Windows-TCPIP",
                TraceEventLevel.Verbose,
                matchAnyKeywords: unchecked((ulong)-1));

            // Subscribe via the dynamic parser, NOT session.Source.AllEvents.
            // Source.AllEvents delivers raw events whose PayloadByName/PayloadNames
            // return nothing because TraceEvent has no manifest cached — that's
            // why the previous "30k data events matched, 0 bytes captured"
            // diagnostic stalled. session.Source.Dynamic auto-loads the WMI/EventLog
            // manifest for any user-mode provider and decodes payloads, giving
            // us real Tcb (HexInt64) and NumBytes (UInt32) fields.
            session.Source.Dynamic.All += OnEtwEvent;

            Log.Information("ETW session started, processing TCP events (Verbose, all keywords, dynamic parser)");
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
        Interlocked.Increment(ref _etwEventsReceived);
        MaybeLogDiscoveredEventType(data);
        try
        {
            var eventId = (int)data.ID;

            // Lifecycle events expose Tcb + ProcessId — populate the Tcb→PID map
            // so subsequent data-transfer events on the same Tcb can be attributed.
            if (eventId is TcpAcceptComplete or TcpConnectComplete)
            {
                Interlocked.Increment(ref _etwLifecycleEventsMatched);
                HandleConnectionEvent(data);
                return;
            }

            // Close/abort/disconnect — drop the Tcb mapping so memory doesn't grow.
            if (eventId is TcpCloseIssued or TcpAbortIssued or TcpAbortCompleted or TcpDisconnectCompleted)
            {
                Interlocked.Increment(ref _etwLifecycleEventsMatched);
                var closingTcb = data.PayloadByName("Tcb")?.ToString();
                if (!string.IsNullOrEmpty(closingTcb))
                    _tcbToConnection.TryRemove(closingTcb, out _);
                return;
            }

            if (eventId is not (TcpDataTransferReceive or TcpDataTransferSend))
                return;

            Interlocked.Increment(ref _etwDataEventsMatched);

            var connTcb = data.PayloadByName("Tcb")?.ToString();
            if (string.IsNullOrEmpty(connTcb)) return;

            var size = 0;
            var sizeObj = data.PayloadByName("NumBytes");
            if (sizeObj is int intSize)
                size = intSize;
            else if (sizeObj is uint uintSize)
                size = (int)uintSize;

            if (size <= 0) return;

            // The Tcb pointer is the connection's stable identity for ETW's
            // entire lifetime. Use it directly as the key — no need to
            // resolve to a "local:port-remote:port" string (the prior design
            // required parsing SocketAddress binary blobs from lifecycle events
            // that don't always fire before the first data event).
            if (_tcbToConnection.TryGetValue(connTcb, out var mapping) && mapping.ProcessId > 0)
            {
                // Cache PID under the same key so GetConnectionStats can look it up.
                _connectionToPid.TryAdd(connTcb, mapping.ProcessId);
            }

            var entry = _connectionBytes.GetOrAdd(connTcb, static _ => new ConnectionBytes());

            if (_connectionBytes.Count > MaxTrackedConnections)
                EvictStaleConnections();

            if (eventId == TcpDataTransferReceive)
                Interlocked.Add(ref entry.BytesReceived, size);
            else
                Interlocked.Add(ref entry.BytesSent, size);

            Interlocked.Add(ref _etwBytesCaptured, size);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error processing ETW event {EventId}", data.ID);
        }
    }

    /// <summary>
    /// Records the Tcb → ProcessId association from a connection lifecycle event.
    /// The Tcb pointer is used directly as the connection's identity key for
    /// subsequent <c>TcpDataTransferSend</c>/<c>TcpDataTransferReceive</c>
    /// attribution; there is no need to parse the SocketAddress binary blobs
    /// for LocalAddress/RemoteAddress.
    /// </summary>
    private void HandleConnectionEvent(TraceEvent data)
    {
        var tcb = data.PayloadByName("Tcb")?.ToString();
        if (string.IsNullOrEmpty(tcb)) return;

        var pidObj = data.PayloadByName("ProcessId");
        int pid = pidObj switch
        {
            int i => i,
            uint u => (int)u,
            _ => 0
        };

        // Evict stale Tcb mappings before adding new ones to bound memory.
        if (_tcbToConnection.Count > MaxTcbMappings)
            EvictStaleTcbMappings();

        _tcbToConnection[tcb] = new TcbMapping(tcb, pid);
        if (pid > 0)
            _connectionToPid[tcb] = pid;
    }

    /// <summary>
    /// Refreshes the mapping of connections to PIDs using GetExtendedTcpTable P/Invoke.
    /// Uses a retry loop to handle ERROR_INSUFFICIENT_BUFFER (122) when the TCP table
    /// grows between the size query and the data fetch.
    /// </summary>
    private void RefreshConnectionPidMapping()
    {
        LogEtwDiagnosticsIfDue();

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

                        if (row.OwningPid != 0 && !_pidToIdentity.ContainsKey(row.OwningPid))
                        {
                            _pidToIdentity[row.OwningPid] = ResolveProcessIdentity(row.OwningPid);
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
                    var identity = _pidToIdentity.GetValueOrDefault(pid, ProcessIdentity.Unknown);
                    processStats = new ProcessConnectionStats
                    {
                        ProcessId = pid,
                        ProcessName = identity.Name,
                        ExecutablePath = identity.Path
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
                    var identity = _pidToIdentity.GetValueOrDefault(pid, ProcessIdentity.Unknown);
                    processStats = new ProcessConnectionStats
                    {
                        ProcessId = pid,
                        ProcessName = identity.Name,
                        ExecutablePath = identity.Path
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
                ErrorMessage = "Failed to collect connection statistics"
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
                    ExecutablePath = p.ExecutablePath,
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
                ErrorMessage = "Failed to collect process statistics"
            };
        }
    }

    internal static string MakeConnectionKey(string localAddr, int localPort, string remoteAddr, int remotePort) =>
        $"{localAddr}:{localPort}-{remoteAddr}:{remotePort}";

    /// <summary>
    /// Removes zero-byte entries to prevent unbounded dictionary growth from high-frequency ETW events.
    /// </summary>
    private void EvictStaleConnections()
    {
        foreach (var kvp in _connectionBytes)
        {
            if (Interlocked.Read(ref kvp.Value.BytesSent) == 0 &&
                Interlocked.Read(ref kvp.Value.BytesReceived) == 0)
            {
                _connectionBytes.TryRemove(kvp.Key, out _);
            }
        }
    }

    /// <summary>
    /// Evicts oldest Tcb mappings to prevent unbounded growth.
    /// </summary>
    private void EvictStaleTcbMappings()
    {
        // Remove mappings for Tcbs that no longer have active byte counters
        foreach (var kvp in _tcbToConnection)
        {
            if (!_connectionBytes.ContainsKey(kvp.Value.ConnectionKey))
            {
                _tcbToConnection.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        Stop();

        // Wait for any in-flight timer callback to complete before disposing
        using var timerDone = new ManualResetEvent(false);
        if (!_refreshTimer.Dispose(timerDone))
            timerDone.WaitOne(TimeSpan.FromSeconds(5));

        _etwSession?.Dispose();
    }

    /// <summary>
    /// On first sighting of each unique event ID, logs the ID, canonical event
    /// name, opcode/task names, and payload field names. Lets us discover which
    /// constants Microsoft-Windows-TCPIP actually uses on the running Windows
    /// build without running PerfView — when 1201/1202 don't match (Windows 11
    /// renamed/renumbered many TCPIP events), we'll see the correct IDs in the
    /// elevation log within seconds of the helper starting.
    /// </summary>
    private void MaybeLogDiscoveredEventType(TraceEvent data)
    {
        if (_discoveredEventIds.Count >= MaxDiscoveredEventTypes)
            return;

        var id = (int)data.ID;
        if (!_discoveredEventIds.TryAdd(id, 0))
            return;

        string payloadFieldNames;
        try
        {
            payloadFieldNames = data.PayloadNames is { Length: > 0 } names
                ? string.Join(", ", names)
                : "<none>";
        }
        catch
        {
            payloadFieldNames = "<error>";
        }

        Log.Information(
            "ETW discovery: id={EventId} name={EventName} task={TaskName} opcode={OpcodeName} payload=[{Fields}]",
            id,
            data.EventName ?? "<null>",
            data.TaskName ?? "<null>",
            data.OpcodeName ?? "<null>",
            payloadFieldNames);
    }

    /// <summary>
    /// Periodically dumps ETW capture statistics to the helper log. Lets us
    /// distinguish "session never produced an event" (provider/keyword problem)
    /// from "events received but none matched our data-transfer IDs"
    /// (OS-version event-ID mismatch) from "events matched but bytes still
    /// zero" (payload parsing problem) — without attaching a debugger.
    /// </summary>
    private void LogEtwDiagnosticsIfDue()
    {
        if (Interlocked.Increment(ref _diagnosticTickCount) % DiagnosticsLogEveryNTicks != 0)
            return;

        Log.Information(
            "ETW diagnostics: events={Events} (data={Data}, lifecycle={Lifecycle}), " +
            "tracked connections={Connections}, captured bytes={Bytes}, " +
            "tcb→key mappings={TcbMappings}, conn→pid mappings={ConnPidMappings}",
            Interlocked.Read(ref _etwEventsReceived),
            Interlocked.Read(ref _etwDataEventsMatched),
            Interlocked.Read(ref _etwLifecycleEventsMatched),
            _connectionBytes.Count,
            Interlocked.Read(ref _etwBytesCaptured),
            _tcbToConnection.Count,
            _connectionToPid.Count);
    }

    /// <summary>
    /// Resolves the process name and full executable path for a PID using the
    /// helper's elevated access. Falls back to <see cref="ProcessIdentity.Unknown"/>
    /// when the process has exited or path resolution is denied (e.g. protected
    /// system processes such as csrss.exe).
    /// </summary>
    private static ProcessIdentity ResolveProcessIdentity(int pid)
    {
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById(pid);
            var name = proc.ProcessName;
            string path = string.Empty;
            try
            {
                path = proc.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                // Access denied (protected process) — keep name, leave path empty
            }
            return new ProcessIdentity(name, path);
        }
        catch
        {
            return ProcessIdentity.Unknown;
        }
    }

    private class ConnectionBytes
    {
        public long BytesSent;
        public long BytesReceived;
    }

    private sealed record TcbMapping(string ConnectionKey, int ProcessId);

    /// <summary>
    /// Cached process identity used to enrich per-process stats with a stable
    /// executable path so app-side consumers can derive a persistent
    /// <c>AppIdentifier</c>.
    /// </summary>
    private sealed record ProcessIdentity(string Name, string Path)
    {
        public static ProcessIdentity Unknown { get; } = new("Unknown", string.Empty);
    }
}

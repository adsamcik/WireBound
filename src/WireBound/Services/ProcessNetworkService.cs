using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using WireBound.Models;

namespace WireBound.Services;

/// <summary>
/// Service for monitoring per-process network statistics using IP Helper API.
/// Tracks TCP/UDP connections per process and estimates traffic based on connection activity.
/// </summary>
public class ProcessNetworkService : IProcessNetworkService
{
    #region P/Invoke Structures and Constants

    private const int AF_INET = 2;
    private const int NO_ERROR = 0;
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
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public int OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPTABLE_OWNER_PID
    {
        public uint NumEntries;
        // Followed by MIB_TCPROW_OWNER_PID entries
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint LocalAddr;
        public uint LocalPort;
        public int OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPTABLE_OWNER_PID
    {
        public uint NumEntries;
        // Followed by MIB_UDPROW_OWNER_PID entries
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        TCP_TABLE_CLASS tableClass,
        uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        UDP_TABLE_CLASS tableClass,
        uint reserved);

    #endregion

    #region Process Info Cache

    private class ProcessInfo
    {
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string AppIdentifier { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public BitmapSource? Icon { get; set; }
        public DateTime CachedAt { get; set; } = DateTime.Now;
    }

    private class ProcessState
    {
        public ProcessInfo Info { get; set; } = null!;
        public int TcpConnectionCount { get; set; }
        public int UdpEndpointCount { get; set; }
        public int PreviousTcpCount { get; set; }
        public int PreviousUdpCount { get; set; }
        public long SessionBytesReceived { get; set; }
        public long SessionBytesSent { get; set; }
        public long DownloadSpeedBps { get; set; }
        public long UploadSpeedBps { get; set; }
        public DateTime FirstSeen { get; set; } = DateTime.Now;
        public DateTime LastSeen { get; set; } = DateTime.Now;
        public DateTime LastPollTime { get; set; } = DateTime.Now;
        public bool IsActive { get; set; } = true;
    }

    #endregion

    private readonly ILogger<ProcessNetworkService> _logger;
    private readonly ConcurrentDictionary<int, ProcessState> _processStates = new();
    private readonly ConcurrentDictionary<int, ProcessInfo> _processInfoCache = new();
    private readonly ConcurrentDictionary<int, DateTime> _recentlyClosedProcesses = new();
    
    private PeriodicTimer? _pollingTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _pollingTask;
    private bool _disposed;

    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RecentlyClosedRetention = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ProcessInfoCacheExpiry = TimeSpan.FromMinutes(5);

    // Estimated bytes per connection activity change (rough heuristic)
    private const long EstimatedBytesPerConnectionChange = 1500; // ~1 MTU

    public bool IsRunning { get; private set; }

    public bool HasRequiredPrivileges
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public event EventHandler<ProcessStatsUpdatedEventArgs>? StatsUpdated;
    public event EventHandler<ProcessNetworkErrorEventArgs>? ErrorOccurred;

    public ProcessNetworkService(ILogger<ProcessNetworkService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> StartAsync()
    {
        if (IsRunning)
        {
            _logger.LogWarning("ProcessNetworkService is already running");
            return true;
        }

        try
        {
            _logger.LogInformation("Starting ProcessNetworkService...");

            _cancellationTokenSource = new CancellationTokenSource();
            _pollingTimer = new PeriodicTimer(PollingInterval);

            // Do an initial poll
            PollConnections();

            _pollingTask = RunPollingLoopAsync(_cancellationTokenSource.Token);
            IsRunning = true;

            _logger.LogInformation("ProcessNetworkService started successfully. HasRequiredPrivileges: {HasPrivileges}",
                HasRequiredPrivileges);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start ProcessNetworkService");
            ErrorOccurred?.Invoke(this, new ProcessNetworkErrorEventArgs(
                "Failed to start process network monitoring", ex));
            return false;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        _logger.LogInformation("Stopping ProcessNetworkService...");

        try
        {
            _cancellationTokenSource?.Cancel();
            _pollingTimer?.Dispose();

            if (_pollingTask != null)
            {
                try
                {
                    await _pollingTask.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Polling task did not complete within timeout");
                }
            }

            IsRunning = false;
            _logger.LogInformation("ProcessNetworkService stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping ProcessNetworkService");
        }
    }

    public IReadOnlyList<ProcessNetworkStats> GetCurrentStats()
    {
        var now = DateTime.Now;
        var stats = new List<ProcessNetworkStats>();

        foreach (var kvp in _processStates)
        {
            var state = kvp.Value;
            
            // Include active processes and recently closed ones
            if (state.IsActive || (now - state.LastSeen) < RecentlyClosedRetention)
            {
                stats.Add(CreateStatsFromState(state));
            }
        }

        return stats.OrderByDescending(s => s.TotalSpeedBps).ToList();
    }

    public IReadOnlyList<ProcessNetworkStats> GetTopProcesses(int count)
    {
        return GetCurrentStats()
            .OrderByDescending(s => s.TotalSpeedBps)
            .ThenByDescending(s => s.TotalSessionBytes)
            .Take(count)
            .ToList();
    }

    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _pollingTimer!.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    PollConnections();
                    CleanupStaleData();

                    var stats = GetCurrentStats();
                    StatsUpdated?.Invoke(this, new ProcessStatsUpdatedEventArgs(stats));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during connection polling");
                    ErrorOccurred?.Invoke(this, new ProcessNetworkErrorEventArgs(
                        "Error polling process connections", ex));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private void PollConnections()
    {
        var now = DateTime.Now;
        var activeProcessIds = new HashSet<int>();

        // Get TCP connections
        var tcpConnections = GetTcpConnectionsByProcess();
        foreach (var (pid, count) in tcpConnections)
        {
            activeProcessIds.Add(pid);
            UpdateProcessState(pid, count, 0, now);
        }

        // Get UDP endpoints
        var udpEndpoints = GetUdpEndpointsByProcess();
        foreach (var (pid, count) in udpEndpoints)
        {
            activeProcessIds.Add(pid);
            UpdateProcessState(pid, 0, count, now, mergeWithExisting: true);
        }

        // Mark inactive processes
        foreach (var kvp in _processStates)
        {
            if (!activeProcessIds.Contains(kvp.Key))
            {
                kvp.Value.IsActive = false;
                kvp.Value.DownloadSpeedBps = 0;
                kvp.Value.UploadSpeedBps = 0;
                
                if (!_recentlyClosedProcesses.ContainsKey(kvp.Key))
                {
                    _recentlyClosedProcesses[kvp.Key] = now;
                }
            }
        }
    }

    private void UpdateProcessState(int pid, int tcpCount, int udpCount, DateTime now, bool mergeWithExisting = false)
    {
        if (pid == 0 || pid == 4) // System/Idle processes
        {
            return;
        }

        var state = _processStates.GetOrAdd(pid, _ => new ProcessState
        {
            Info = GetOrCreateProcessInfo(pid),
            FirstSeen = now,
            LastPollTime = now
        });

        state.LastSeen = now;
        state.IsActive = true;

        // Remove from recently closed if it's active again
        _recentlyClosedProcesses.TryRemove(pid, out _);

        if (mergeWithExisting)
        {
            state.UdpEndpointCount = udpCount;
        }
        else
        {
            state.TcpConnectionCount = tcpCount;
        }

        // Calculate estimated traffic based on connection changes
        var elapsed = (now - state.LastPollTime).TotalSeconds;
        if (elapsed > 0)
        {
            var tcpDelta = Math.Abs(tcpCount - state.PreviousTcpCount);
            var udpDelta = Math.Abs(udpCount - state.PreviousUdpCount);
            var totalActivity = tcpDelta + udpDelta;

            if (totalActivity > 0)
            {
                // Rough estimation: each connection change implies some data transfer
                var estimatedBytes = totalActivity * EstimatedBytesPerConnectionChange;
                
                // Split 60/40 between download and upload (typical browsing pattern)
                var downloadBytes = (long)(estimatedBytes * 0.6);
                var uploadBytes = estimatedBytes - downloadBytes;

                state.DownloadSpeedBps = (long)(downloadBytes / elapsed);
                state.UploadSpeedBps = (long)(uploadBytes / elapsed);
                state.SessionBytesReceived += downloadBytes;
                state.SessionBytesSent += uploadBytes;
            }
            else
            {
                // Decay speed if no connection changes (with minimum threshold)
                state.DownloadSpeedBps = (long)(state.DownloadSpeedBps * 0.5);
                state.UploadSpeedBps = (long)(state.UploadSpeedBps * 0.5);
                
                if (state.DownloadSpeedBps < 100) state.DownloadSpeedBps = 0;
                if (state.UploadSpeedBps < 100) state.UploadSpeedBps = 0;
            }

            state.PreviousTcpCount = tcpCount;
            state.PreviousUdpCount = udpCount;
            state.LastPollTime = now;
        }
    }

    private ProcessInfo GetOrCreateProcessInfo(int pid)
    {
        var now = DateTime.Now;

        // Check cache first
        if (_processInfoCache.TryGetValue(pid, out var cached))
        {
            if ((now - cached.CachedAt) < ProcessInfoCacheExpiry)
            {
                return cached;
            }
        }

        var info = new ProcessInfo
        {
            ProcessId = pid,
            CachedAt = now
        };

        try
        {
            using var process = Process.GetProcessById(pid);
            info.ProcessName = process.ProcessName;
            info.DisplayName = GetDisplayName(process);

            try
            {
                info.ExecutablePath = process.MainModule?.FileName ?? string.Empty;
            }
            catch (Exception ex)
            {
                // Access denied or 32/64-bit mismatch
                _logger.LogDebug(ex, "Could not get executable path for PID {Pid}", pid);
                info.ExecutablePath = string.Empty;
            }

            if (!string.IsNullOrEmpty(info.ExecutablePath))
            {
                info.AppIdentifier = ComputeAppIdentifier(info.ExecutablePath);
                info.Icon = ExtractIcon(info.ExecutablePath);
            }
            else
            {
                info.AppIdentifier = ComputeAppIdentifier($"{info.ProcessName}_{pid}");
            }
        }
        catch (ArgumentException)
        {
            // Process no longer exists
            info.ProcessName = $"[PID {pid}]";
            info.DisplayName = info.ProcessName;
            info.AppIdentifier = ComputeAppIdentifier($"unknown_{pid}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting process info for PID {Pid}", pid);
            info.ProcessName = $"[PID {pid}]";
            info.DisplayName = info.ProcessName;
            info.AppIdentifier = ComputeAppIdentifier($"error_{pid}");
        }

        _processInfoCache[pid] = info;
        return info;
    }

    private static string GetDisplayName(Process process)
    {
        try
        {
            // Try to get FileDescription from version info
            var mainModule = process.MainModule;
            if (mainModule != null)
            {
                var versionInfo = mainModule.FileVersionInfo;
                if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
                {
                    return versionInfo.FileDescription;
                }
                if (!string.IsNullOrWhiteSpace(versionInfo.ProductName))
                {
                    return versionInfo.ProductName;
                }
            }
        }
        catch
        {
            // Ignore - will fall back to process name
        }

        return process.ProcessName;
    }

    private static string ComputeAppIdentifier(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private BitmapSource? ExtractIcon(string executablePath)
    {
        try
        {
            if (string.IsNullOrEmpty(executablePath) || !System.IO.File.Exists(executablePath))
            {
                return null;
            }

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
            if (icon == null)
            {
                return null;
            }

            // Convert to BitmapSource on UI thread
            return Application.Current?.Dispatcher.Invoke(() =>
            {
                var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmapSource.Freeze(); // Make it thread-safe
                return bitmapSource;
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not extract icon from {Path}", executablePath);
            return null;
        }
    }

    private Dictionary<int, int> GetTcpConnectionsByProcess()
    {
        var result = new Dictionary<int, int>();
        IntPtr tablePtr = IntPtr.Zero;

        try
        {
            int size = 0;
            var ret = GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET,
                TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

            if (ret != ERROR_INSUFFICIENT_BUFFER)
            {
                return result;
            }

            tablePtr = Marshal.AllocHGlobal(size);
            ret = GetExtendedTcpTable(tablePtr, ref size, false, AF_INET,
                TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

            if (ret != NO_ERROR)
            {
                return result;
            }

            var table = Marshal.PtrToStructure<MIB_TCPTABLE_OWNER_PID>(tablePtr);
            var rowPtr = IntPtr.Add(tablePtr, Marshal.SizeOf<uint>()); // Skip NumEntries

            for (int i = 0; i < table.NumEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                
                if (!result.ContainsKey(row.OwningPid))
                {
                    result[row.OwningPid] = 0;
                }
                result[row.OwningPid]++;

                rowPtr = IntPtr.Add(rowPtr, Marshal.SizeOf<MIB_TCPROW_OWNER_PID>());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting TCP connections");
        }
        finally
        {
            if (tablePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(tablePtr);
            }
        }

        return result;
    }

    private Dictionary<int, int> GetUdpEndpointsByProcess()
    {
        var result = new Dictionary<int, int>();
        IntPtr tablePtr = IntPtr.Zero;

        try
        {
            int size = 0;
            var ret = GetExtendedUdpTable(IntPtr.Zero, ref size, false, AF_INET,
                UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);

            if (ret != ERROR_INSUFFICIENT_BUFFER)
            {
                return result;
            }

            tablePtr = Marshal.AllocHGlobal(size);
            ret = GetExtendedUdpTable(tablePtr, ref size, false, AF_INET,
                UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);

            if (ret != NO_ERROR)
            {
                return result;
            }

            var table = Marshal.PtrToStructure<MIB_UDPTABLE_OWNER_PID>(tablePtr);
            var rowPtr = IntPtr.Add(tablePtr, Marshal.SizeOf<uint>()); // Skip NumEntries

            for (int i = 0; i < table.NumEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                
                if (!result.ContainsKey(row.OwningPid))
                {
                    result[row.OwningPid] = 0;
                }
                result[row.OwningPid]++;

                rowPtr = IntPtr.Add(rowPtr, Marshal.SizeOf<MIB_UDPROW_OWNER_PID>());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting UDP endpoints");
        }
        finally
        {
            if (tablePtr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(tablePtr);
            }
        }

        return result;
    }

    private ProcessNetworkStats CreateStatsFromState(ProcessState state)
    {
        return new ProcessNetworkStats
        {
            ProcessId = state.Info.ProcessId,
            ProcessName = state.Info.ProcessName,
            ExecutablePath = state.Info.ExecutablePath,
            AppIdentifier = state.Info.AppIdentifier,
            DisplayName = state.Info.DisplayName,
            DownloadSpeedBps = state.DownloadSpeedBps,
            UploadSpeedBps = state.UploadSpeedBps,
            SessionBytesReceived = state.SessionBytesReceived,
            SessionBytesSent = state.SessionBytesSent,
            FirstSeen = state.FirstSeen,
            LastSeen = state.LastSeen,
            Icon = state.Info.Icon
        };
    }

    private void CleanupStaleData()
    {
        var now = DateTime.Now;
        var expiredPids = new List<int>();

        // Clean up recently closed processes that have exceeded retention
        foreach (var kvp in _recentlyClosedProcesses)
        {
            if ((now - kvp.Value) > RecentlyClosedRetention)
            {
                expiredPids.Add(kvp.Key);
            }
        }

        foreach (var pid in expiredPids)
        {
            _recentlyClosedProcesses.TryRemove(pid, out _);
            _processStates.TryRemove(pid, out _);
        }

        // Clean up stale process info cache
        var expiredCache = new List<int>();
        foreach (var kvp in _processInfoCache)
        {
            if ((now - kvp.Value.CachedAt) > ProcessInfoCacheExpiry * 2)
            {
                expiredCache.Add(kvp.Key);
            }
        }

        foreach (var pid in expiredCache)
        {
            _processInfoCache.TryRemove(pid, out _);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        
        try
        {
            _cancellationTokenSource?.Cancel();
            _pollingTimer?.Dispose();
            _cancellationTokenSource?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during ProcessNetworkService disposal");
        }

        IsRunning = false;
        _processStates.Clear();
        _processInfoCache.Clear();
        _recentlyClosedProcesses.Clear();

        GC.SuppressFinalize(this);
    }
}

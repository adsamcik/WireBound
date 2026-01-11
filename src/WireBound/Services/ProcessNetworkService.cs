using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Microsoft.Extensions.Logging;
using WireBound.Models;

namespace WireBound.Services;

/// <summary>
/// Service for monitoring per-process network statistics using Windows IP Helper API.
/// Uses modern LibraryImport source generators for NativeAOT compatibility with iphlpapi.dll.
/// </summary>
public sealed partial class ProcessNetworkService : IProcessNetworkService
{
    private readonly ILogger<ProcessNetworkService> _logger;
    private readonly ConcurrentDictionary<int, ProcessNetworkStats> _processStats = new();
    private readonly ConcurrentDictionary<int, ProcessInfo> _processInfoCache = new();
    private readonly ConcurrentDictionary<int, DateTime> _closedProcesses = new();
    
    private const int MaxCacheSize = 1000;
    private const int MaxInterfaceEntries = 1000;
    
    private PeriodicTimer? _pollingTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _pollingTask;
    private bool _disposed;
    
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ClosedProcessRetentionTime = TimeSpan.FromSeconds(30);
    
    public bool IsRunning { get; private set; }
    
    public bool HasRequiredPrivileges => CheckAdminPrivileges();
    
    public bool IsPlatformSupported =>
#if WINDOWS
        true;
#else
        false;
#endif
    
    public event EventHandler<ProcessStatsUpdatedEventArgs>? StatsUpdated;
    public event EventHandler<ProcessNetworkErrorEventArgs>? ErrorOccurred;
    
    public ProcessNetworkService(ILogger<ProcessNetworkService> logger)
    {
        _logger = logger;
    }
    
    public async Task<bool> StartAsync()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ProcessNetworkService));
        }
        
        if (IsRunning)
        {
            _logger.LogWarning("ProcessNetworkService is already running");
            return true;
        }
        
        if (!IsPlatformSupported)
        {
            _logger.LogWarning("Per-process network monitoring is not supported on this platform");
            ErrorOccurred?.Invoke(this, new ProcessNetworkErrorEventArgs(
                "Per-process network monitoring is only supported on Windows"));
            return false;
        }
        
        if (!HasRequiredPrivileges)
        {
            _logger.LogWarning("Insufficient privileges for per-process network monitoring");
            ErrorOccurred?.Invoke(this, new ProcessNetworkErrorEventArgs(
                "Administrator privileges required for per-process network monitoring",
                requiresElevation: true));
            return false;
        }
        
        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _pollingTimer = new PeriodicTimer(PollingInterval);
            
            _pollingTask = RunPollingLoopAsync(_cancellationTokenSource.Token);
            IsRunning = true;
            
            _logger.LogInformation("ProcessNetworkService started successfully");
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
        
        try
        {
            _cancellationTokenSource?.Cancel();
            
            if (_pollingTask != null)
            {
                try
                {
                    await _pollingTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancelling
                }
            }
            
            _pollingTimer?.Dispose();
            _pollingTimer = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            
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
        CleanupClosedProcesses();
        return _processStats.Values.ToList().AsReadOnly();
    }
    
    public IReadOnlyList<ProcessNetworkStats> GetTopProcesses(int count)
    {
        CleanupClosedProcesses();
        return _processStats.Values
            .OrderByDescending(p => p.TotalSpeedBps)
            .Take(count)
            .ToList()
            .AsReadOnly();
    }
    
    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting polling loop");
        
        // Track previous connection counts per process for calculating deltas
        var previousStats = new Dictionary<int, (long BytesReceived, long BytesSent)>();
        
        try
        {
            while (await _pollingTimer!.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    var connectionsByProcess = GetConnectionsByProcess();
                    var currentTime = DateTime.Now;
                    var activeProcessIds = new HashSet<int>();
                    
                    foreach (var (pid, connections) in connectionsByProcess)
                    {
                        if (pid == 0 || pid == 4) // Skip System and System Idle processes
                        {
                            continue;
                        }
                        
                        activeProcessIds.Add(pid);
                        
                        var processInfo = GetOrCacheProcessInfo(pid);
                        if (processInfo == null)
                        {
                            continue;
                        }
                        
                        // Calculate total connection count (as a rough proxy for activity)
                        var totalConnections = connections.TcpCount + connections.UdpCount;
                        
                        var stats = _processStats.GetOrAdd(pid, _ => new ProcessNetworkStats
                        {
                            ProcessId = pid,
                            ProcessName = processInfo.Name,
                            ExecutablePath = processInfo.Path,
                            AppIdentifier = processInfo.AppIdentifier,
                            DisplayName = processInfo.DisplayName,
                            IconBase64 = processInfo.IconBase64,
                            FirstSeen = currentTime
                        });
                        
                        // Update last seen
                        stats.LastSeen = currentTime;
                        
                        // Estimate bytes based on connection activity
                        // This is a simplified model - actual bytes would require ETW or performance counters
                        // For now, we track connection counts as a proxy
                        var estimatedBytesReceived = connections.TcpCount * 1024L; // Rough estimate
                        var estimatedBytesSent = connections.UdpCount * 512L;
                        
                        if (previousStats.TryGetValue(pid, out var prev))
                        {
                            var receivedDelta = Math.Max(0, estimatedBytesReceived - prev.BytesReceived);
                            var sentDelta = Math.Max(0, estimatedBytesSent - prev.BytesSent);
                            
                            stats.DownloadSpeedBps = receivedDelta;
                            stats.UploadSpeedBps = sentDelta;
                            stats.SessionBytesReceived += receivedDelta;
                            stats.SessionBytesSent += sentDelta;
                        }
                        
                        previousStats[pid] = (estimatedBytesReceived, estimatedBytesSent);
                        
                        // Remove from closed processes if it was there
                        _closedProcesses.TryRemove(pid, out _);
                    }
                    
                    // Mark processes that are no longer active
                    foreach (var pid in _processStats.Keys.Except(activeProcessIds))
                    {
                        if (_processStats.TryGetValue(pid, out var stats))
                        {
                            stats.DownloadSpeedBps = 0;
                            stats.UploadSpeedBps = 0;
                        }
                        
                        _closedProcesses.TryAdd(pid, currentTime);
                    }
                    
                    CleanupClosedProcesses();
                    EnforceCacheLimits();
                    
                    // Raise event with updated stats
                    var currentStats = GetCurrentStats();
                    StatsUpdated?.Invoke(this, new ProcessStatsUpdatedEventArgs(currentStats));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during polling iteration");
                    ErrorOccurred?.Invoke(this, new ProcessNetworkErrorEventArgs(
                        "Error collecting process network statistics", ex));
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Polling loop cancelled");
        }
    }
    
    private void CleanupClosedProcesses()
    {
        var cutoffTime = DateTime.Now - ClosedProcessRetentionTime;
        
        foreach (var (pid, closedTime) in _closedProcesses)
        {
            if (closedTime < cutoffTime)
            {
                _processStats.TryRemove(pid, out _);
                _processInfoCache.TryRemove(pid, out _);
                _closedProcesses.TryRemove(pid, out _);
            }
        }
    }
    
    /// <summary>
    /// Enforces cache size limits using LRU eviction based on LastSeen timestamp.
    /// Removes oldest entries when cache exceeds MaxCacheSize.
    /// </summary>
    private void EnforceCacheLimits()
    {
        if (_processStats.Count <= MaxCacheSize)
        {
            return;
        }
        
        // Get entries sorted by LastSeen (oldest first) and remove excess
        var entriesToRemove = _processStats
            .OrderBy(kvp => kvp.Value.LastSeen)
            .Take(_processStats.Count - MaxCacheSize)
            .Select(kvp => kvp.Key)
            .ToList();
        
        foreach (var pid in entriesToRemove)
        {
            _processStats.TryRemove(pid, out _);
            _processInfoCache.TryRemove(pid, out _);
            _closedProcesses.TryRemove(pid, out _);
        }
        
        _logger.LogDebug("Evicted {Count} entries from process cache due to size limit", entriesToRemove.Count);
    }
    
    private ProcessInfo? GetOrCacheProcessInfo(int pid)
    {
        // Use TryGetValue + TryAdd pattern to avoid caching null/failed lookups
        if (_processInfoCache.TryGetValue(pid, out var cachedInfo))
        {
            return cachedInfo;
        }
        
        try
        {
            using var process = Process.GetProcessById(pid);
            var path = GetProcessPath(process);
            var name = process.ProcessName;
            var displayName = GetDisplayName(process, path);
            var appIdentifier = ComputeAppIdentifier(path);
            var iconBase64 = ExtractIconBase64(path);
            
            var processInfo = new ProcessInfo
            {
                Name = name,
                Path = path,
                DisplayName = displayName,
                AppIdentifier = appIdentifier,
                IconBase64 = iconBase64
            };
            
            // Only cache successful lookups - don't cache null values
            _processInfoCache.TryAdd(pid, processInfo);
            return processInfo;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get process info for PID {Pid}", pid);
            return null;
        }
    }
    
    private static string GetProcessPath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            // Access denied or process exited
            return string.Empty;
        }
    }
    
    private static string GetDisplayName(Process process, string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrEmpty(versionInfo.FileDescription))
                {
                    return versionInfo.FileDescription;
                }
                if (!string.IsNullOrEmpty(versionInfo.ProductName))
                {
                    return versionInfo.ProductName;
                }
            }
        }
        catch
        {
            // Ignore errors getting version info
        }
        
        return process.ProcessName;
    }
    
    private static string ComputeAppIdentifier(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }
        
        var bytes = Encoding.UTF8.GetBytes(path.ToLowerInvariant());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
    
    private string? ExtractIconBase64(string path)
    {
#if WINDOWS
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }
            
            var shfileinfo = new SHFILEINFO();
            var flags = SHGFI_ICON | SHGFI_SMALLICON;
            
            var result = SHGetFileInfo(path, 0, ref shfileinfo, (uint)Marshal.SizeOf(shfileinfo), flags);
            
            if (result == IntPtr.Zero || shfileinfo.hIcon == IntPtr.Zero)
            {
                return null;
            }
            
            try
            {
                using var icon = System.Drawing.Icon.FromHandle(shfileinfo.hIcon);
                using var bitmap = icon.ToBitmap();
                using var ms = new MemoryStream();
                bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                return Convert.ToBase64String(ms.ToArray());
            }
            finally
            {
                DestroyIcon(shfileinfo.hIcon);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to extract icon for {Path}", path);
            return null;
        }
#else
        return null;
#endif
    }
    
    private static bool CheckAdminPrivileges()
    {
#if WINDOWS
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
#else
        return false;
#endif
    }
    
    private Dictionary<int, ConnectionInfo> GetConnectionsByProcess()
    {
#if WINDOWS
        var result = new Dictionary<int, ConnectionInfo>();
        
        try
        {
            // Get TCP connections
            GetTcpConnections(result);
            
            // Get UDP endpoints
            GetUdpEndpoints(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get network connections");
        }
        
        return result;
#else
        return new Dictionary<int, ConnectionInfo>();
#endif
    }
    
#if WINDOWS
    private void GetTcpConnections(Dictionary<int, ConnectionInfo> result)
    {
        int bufferSize = 0;
        
        // First call to get buffer size
        var ret = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        
        if (ret != ERROR_INSUFFICIENT_BUFFER && ret != NO_ERROR)
        {
            _logger.LogWarning("GetExtendedTcpTable failed with error {Error}", ret);
            return;
        }
        
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            ret = GetExtendedTcpTable(buffer, ref bufferSize, false, AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
            
            if (ret != NO_ERROR)
            {
                _logger.LogWarning("GetExtendedTcpTable failed with error {Error}", ret);
                return;
            }
            
            var numEntries = Marshal.ReadInt32(buffer);
            
            // Validate entry count is within reasonable bounds to prevent buffer overruns
            if (numEntries < 0 || numEntries > MaxInterfaceEntries)
            {
                _logger.LogWarning("GetExtendedTcpTable returned invalid entry count: {Count}", numEntries);
                return;
            }
            
            var rowPtr = buffer + sizeof(int);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            
            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                
                if (!result.TryGetValue(row.dwOwningPid, out var info))
                {
                    info = new ConnectionInfo();
                    result[row.dwOwningPid] = info;
                }
                
                info.TcpCount++;
                
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
    
    private void GetUdpEndpoints(Dictionary<int, ConnectionInfo> result)
    {
        int bufferSize = 0;
        
        // First call to get buffer size
        var ret = GetExtendedUdpTable(IntPtr.Zero, ref bufferSize, false, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
        
        if (ret != ERROR_INSUFFICIENT_BUFFER && ret != NO_ERROR)
        {
            _logger.LogWarning("GetExtendedUdpTable failed with error {Error}", ret);
            return;
        }
        
        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            ret = GetExtendedUdpTable(buffer, ref bufferSize, false, AF_INET, UDP_TABLE_CLASS.UDP_TABLE_OWNER_PID, 0);
            
            if (ret != NO_ERROR)
            {
                _logger.LogWarning("GetExtendedUdpTable failed with error {Error}", ret);
                return;
            }
            
            var numEntries = Marshal.ReadInt32(buffer);
            
            // Validate entry count is within reasonable bounds to prevent buffer overruns
            if (numEntries < 0 || numEntries > MaxInterfaceEntries)
            {
                _logger.LogWarning("GetExtendedUdpTable returned invalid entry count: {Count}", numEntries);
                return;
            }
            
            var rowPtr = buffer + sizeof(int);
            var rowSize = Marshal.SizeOf<MIB_UDPROW_OWNER_PID>();
            
            for (int i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_UDPROW_OWNER_PID>(rowPtr);
                
                if (!result.TryGetValue(row.dwOwningPid, out var info))
                {
                    info = new ConnectionInfo();
                    result[row.dwOwningPid] = info;
                }
                
                info.UdpCount++;
                
                rowPtr += rowSize;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
#endif
    
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        
        _disposed = true;
        
        _cancellationTokenSource?.Cancel();
        _pollingTimer?.Dispose();
        _cancellationTokenSource?.Dispose();
        
        _processStats.Clear();
        _processInfoCache.Clear();
        _closedProcesses.Clear();
        
        _logger.LogInformation("ProcessNetworkService disposed");
    }
    
    #region Helper Classes
    
    private sealed class ProcessInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string AppIdentifier { get; set; } = string.Empty;
        public string? IconBase64 { get; set; }
    }
    
    private sealed class ConnectionInfo
    {
        public int TcpCount { get; set; }
        public int UdpCount { get; set; }
    }
    
    #endregion
    
    #region P/Invoke Definitions
    
#if WINDOWS
    private const int AF_INET = 2;
    private const int NO_ERROR = 0;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    
    // Shell32 constants for icon extraction
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    
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
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public int dwOwningPid;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPTABLE_OWNER_PID
    {
        public uint dwNumEntries;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public MIB_TCPROW_OWNER_PID[] table;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public int dwOwningPid;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPTABLE_OWNER_PID
    {
        public uint dwNumEntries;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public MIB_UDPROW_OWNER_PID[] table;
    }
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
    
    // Modern LibraryImport source generators for NativeAOT compatibility
    // These generate marshalling code at compile time instead of runtime
    
    [LibraryImport("iphlpapi.dll", SetLastError = true)]
    private static partial int GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        int ulAf,
        TCP_TABLE_CLASS TableClass,
        uint Reserved);
    
    [LibraryImport("iphlpapi.dll", SetLastError = true)]
    private static partial int GetExtendedUdpTable(
        IntPtr pUdpTable,
        ref int pdwSize,
        [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        int ulAf,
        UDP_TABLE_CLASS TableClass,
        uint Reserved);
    
    // SHGetFileInfo uses complex string marshalling - keeping DllImport for compatibility
    // TODO: Convert to LibraryImport with explicit string marshalling when struct support improves
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);
    
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr hIcon);
#endif
    
    #endregion
}

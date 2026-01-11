using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WireBound.Models;

namespace WireBound.Services;

/// <summary>
/// Network monitoring service using System.Net.NetworkInformation with IP Helper API fallback
/// </summary>
public sealed class NetworkMonitorService : INetworkMonitorService
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AdapterState> _adapterStates = new();
    private readonly ILogger<NetworkMonitorService> _logger;
    private string _selectedAdapterId = string.Empty;
    private bool _useIpHelperApi = false;
    private NetworkStats _currentStats = new();
    private DateTime _sessionStart = DateTime.Now;

    public event EventHandler<NetworkStats>? StatsUpdated;

    public bool IsUsingIpHelperApi => _useIpHelperApi;

    public NetworkMonitorService(ILogger<NetworkMonitorService> logger)
    {
        _logger = logger;
        InitializeAdapters();
    }

    private void InitializeAdapters()
    {
        lock (_lock)
        {
            _adapterStates.Clear();
            
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus == OperationalStatus.Up && 
                    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var stats = nic.GetIPv4Statistics();
                    _adapterStates[nic.Id] = new AdapterState
                    {
                        Adapter = MapAdapter(nic),
                        LastBytesReceived = stats.BytesReceived,
                        LastBytesSent = stats.BytesSent,
                        SessionStartReceived = stats.BytesReceived,
                        SessionStartSent = stats.BytesSent,
                        LastPollTime = DateTime.Now
                    };

                    _logger.LogInformation("Discovered network adapter: {AdapterName} ({AdapterId})",
                        nic.Name, nic.Id);
                }
            }

            _logger.LogInformation("Initialized {AdapterCount} network adapters", _adapterStates.Count);
        }
    }

    public IReadOnlyList<NetworkAdapter> GetAdapters()
    {
        lock (_lock)
        {
            return _adapterStates.Values.Select(s => s.Adapter).ToList();
        }
    }

    public NetworkStats GetCurrentStats() => _currentStats;

    public NetworkStats GetStats(string adapterId)
    {
        lock (_lock)
        {
            if (_adapterStates.TryGetValue(adapterId, out var state))
            {
                return CreateStats(state);
            }
            return new NetworkStats();
        }
    }

    public void SetAdapter(string adapterId)
    {
        lock (_lock)
        {
            _selectedAdapterId = adapterId;
        }
    }

    public void SetUseIpHelperApi(bool useIpHelper)
    {
        lock (_lock)
        {
            _useIpHelperApi = useIpHelper;
        }
    }

    public void Poll()
    {
        if (_useIpHelperApi)
        {
            PollWithIpHelper();
        }
        else
        {
            PollWithDotNet();
        }
    }

    private void PollWithDotNet()
    {
        lock (_lock)
        {
            var now = DateTime.Now;
            long totalDownloadSpeed = 0;
            long totalUploadSpeed = 0;
            long totalSessionReceived = 0;
            long totalSessionSent = 0;

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!_adapterStates.TryGetValue(nic.Id, out var state))
                    continue;

                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                try
                {
                    var stats = nic.GetIPv4Statistics();
                    var elapsed = (now - state.LastPollTime).TotalSeconds;
                    
                    if (elapsed > 0)
                    {
                        var bytesReceivedDelta = stats.BytesReceived - state.LastBytesReceived;
                        var bytesSentDelta = stats.BytesSent - state.LastBytesSent;

                        // Handle counter reset (system reboot)
                        if (bytesReceivedDelta < 0) bytesReceivedDelta = stats.BytesReceived;
                        if (bytesSentDelta < 0) bytesSentDelta = stats.BytesSent;

                        state.DownloadSpeedBps = (long)(bytesReceivedDelta / elapsed);
                        state.UploadSpeedBps = (long)(bytesSentDelta / elapsed);
                        state.LastBytesReceived = stats.BytesReceived;
                        state.LastBytesSent = stats.BytesSent;
                        state.LastPollTime = now;

                        // Aggregate if no specific adapter selected
                        if (string.IsNullOrEmpty(_selectedAdapterId) || _selectedAdapterId == nic.Id)
                        {
                            totalDownloadSpeed += state.DownloadSpeedBps;
                            totalUploadSpeed += state.UploadSpeedBps;
                            totalSessionReceived += stats.BytesReceived - state.SessionStartReceived;
                            totalSessionSent += stats.BytesSent - state.SessionStartSent;
                        }
                    }
                }
                catch (NetworkInformationException ex)
                {
                    // Adapter might have been disconnected
                    _logger.LogWarning(ex, "Error reading stats for adapter {AdapterId}", nic.Id);
                }
            }

            _currentStats = new NetworkStats
            {
                Timestamp = now,
                DownloadSpeedBps = totalDownloadSpeed,
                UploadSpeedBps = totalUploadSpeed,
                SessionBytesReceived = totalSessionReceived,
                SessionBytesSent = totalSessionSent,
                AdapterId = _selectedAdapterId
            };

            StatsUpdated?.Invoke(this, _currentStats);
        }
    }

    private void PollWithIpHelper()
    {
        // Use IP Helper API for more robust monitoring
        lock (_lock)
        {
            var now = DateTime.Now;
            var table = IpHelperApi.GetIfTable2();
            
            long totalDownloadSpeed = 0;
            long totalUploadSpeed = 0;
            long totalSessionReceived = 0;
            long totalSessionSent = 0;

            foreach (var row in table)
            {
                var adapterId = row.InterfaceIndex.ToString();
                
                if (!_adapterStates.TryGetValue(adapterId, out var state))
                {
                    // New adapter discovered
                    _adapterStates[adapterId] = state = new AdapterState
                    {
                        Adapter = new NetworkAdapter
                        {
                            Id = adapterId,
                            Name = row.Description,
                            Description = row.Alias,
                            IsActive = row.OperStatus == 1
                        },
                        LastBytesReceived = (long)row.InOctets,
                        LastBytesSent = (long)row.OutOctets,
                        SessionStartReceived = (long)row.InOctets,
                        SessionStartSent = (long)row.OutOctets,
                        LastPollTime = now
                    };

                    _logger.LogInformation("IP Helper API discovered new adapter: {AdapterName} (Index: {AdapterId})",
                        row.Description, adapterId);
                    continue;
                }

                if (row.OperStatus != 1) continue; // Not operational

                var elapsed = (now - state.LastPollTime).TotalSeconds;
                if (elapsed > 0)
                {
                    var bytesReceivedDelta = (long)row.InOctets - state.LastBytesReceived;
                    var bytesSentDelta = (long)row.OutOctets - state.LastBytesSent;

                    if (bytesReceivedDelta < 0) bytesReceivedDelta = (long)row.InOctets;
                    if (bytesSentDelta < 0) bytesSentDelta = (long)row.OutOctets;

                    state.DownloadSpeedBps = (long)(bytesReceivedDelta / elapsed);
                    state.UploadSpeedBps = (long)(bytesSentDelta / elapsed);
                    state.LastBytesReceived = (long)row.InOctets;
                    state.LastBytesSent = (long)row.OutOctets;
                    state.LastPollTime = now;

                    if (string.IsNullOrEmpty(_selectedAdapterId) || _selectedAdapterId == adapterId)
                    {
                        totalDownloadSpeed += state.DownloadSpeedBps;
                        totalUploadSpeed += state.UploadSpeedBps;
                        totalSessionReceived += (long)row.InOctets - state.SessionStartReceived;
                        totalSessionSent += (long)row.OutOctets - state.SessionStartSent;
                    }
                }
            }

            _currentStats = new NetworkStats
            {
                Timestamp = now,
                DownloadSpeedBps = totalDownloadSpeed,
                UploadSpeedBps = totalUploadSpeed,
                SessionBytesReceived = totalSessionReceived,
                SessionBytesSent = totalSessionSent,
                AdapterId = _selectedAdapterId
            };

            StatsUpdated?.Invoke(this, _currentStats);
        }
    }

    public void ResetSession()
    {
        lock (_lock)
        {
            _sessionStart = DateTime.Now;
            foreach (var state in _adapterStates.Values)
            {
                state.SessionStartReceived = state.LastBytesReceived;
                state.SessionStartSent = state.LastBytesSent;
            }
        }
    }

    private static NetworkAdapter MapAdapter(NetworkInterface nic)
    {
        return new NetworkAdapter
        {
            Id = nic.Id,
            Name = nic.Name,
            Description = nic.Description,
            IsActive = nic.OperationalStatus == OperationalStatus.Up,
            Speed = nic.Speed,
            AdapterType = nic.NetworkInterfaceType switch
            {
                NetworkInterfaceType.Ethernet => NetworkAdapterType.Ethernet,
                NetworkInterfaceType.Wireless80211 => NetworkAdapterType.WiFi,
                NetworkInterfaceType.Loopback => NetworkAdapterType.Loopback,
                NetworkInterfaceType.Tunnel => NetworkAdapterType.Tunnel,
                _ => NetworkAdapterType.Other
            }
        };
    }

    private static NetworkStats CreateStats(AdapterState state)
    {
        return new NetworkStats
        {
            Timestamp = DateTime.Now,
            DownloadSpeedBps = state.DownloadSpeedBps,
            UploadSpeedBps = state.UploadSpeedBps,
            SessionBytesReceived = state.LastBytesReceived - state.SessionStartReceived,
            SessionBytesSent = state.LastBytesSent - state.SessionStartSent,
            AdapterId = state.Adapter.Id
        };
    }

    private class AdapterState
    {
        public NetworkAdapter Adapter { get; set; } = new();
        public long LastBytesReceived { get; set; }
        public long LastBytesSent { get; set; }
        public long SessionStartReceived { get; set; }
        public long SessionStartSent { get; set; }
        public DateTime LastPollTime { get; set; }
        public long DownloadSpeedBps { get; set; }
        public long UploadSpeedBps { get; set; }
    }
}

/// <summary>
/// P/Invoke wrapper for IP Helper API (GetIfTable2).
/// Uses modern LibraryImport source generators for NativeAOT compatibility.
/// Note: Complex structs with ByValTStr require manual marshalling for full AOT support.
/// </summary>
internal static partial class IpHelperApi
{
    [LibraryImport("iphlpapi.dll", SetLastError = true)]
    private static partial int GetIfTable2(out IntPtr table);

    [LibraryImport("iphlpapi.dll", SetLastError = true)]
    private static partial void FreeMibTable(IntPtr table);

    // MIB_IF_ROW2 is 1352 bytes on x64. We must match exactly to avoid memory corruption.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MIB_IF_ROW2
    {
        public long InterfaceLuid;                              // 8 bytes
        public uint InterfaceIndex;                             // 4 bytes
        public Guid InterfaceGuid;                              // 16 bytes
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
        public string Alias;                                    // 514 bytes (257 * 2)
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 257)]
        public string Description;                              // 514 bytes (257 * 2)
        public uint PhysicalAddressLength;                      // 4 bytes
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] PhysicalAddress;                          // 32 bytes
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] PermanentPhysicalAddress;                 // 32 bytes
        public uint Mtu;                                        // 4 bytes
        public uint Type;                                       // 4 bytes
        public uint TunnelType;                                 // 4 bytes
        public uint MediaType;                                  // 4 bytes
        public uint PhysicalMediumType;                         // 4 bytes
        public uint AccessType;                                 // 4 bytes
        public uint DirectionType;                              // 4 bytes
        public uint InterfaceAndOperStatusFlags;                // 4 bytes
        public uint OperStatus;                                 // 4 bytes
        public uint AdminStatus;                                // 4 bytes
        public uint MediaConnectState;                          // 4 bytes
        public Guid NetworkGuid;                                // 16 bytes
        public uint ConnectionType;                             // 4 bytes
        private uint _padding;                                  // 4 bytes padding for 8-byte alignment
        public ulong TransmitLinkSpeed;                         // 8 bytes
        public ulong ReceiveLinkSpeed;                          // 8 bytes
        public ulong InOctets;                                  // 8 bytes
        public ulong InUcastPkts;                               // 8 bytes
        public ulong InNUcastPkts;                              // 8 bytes
        public ulong InDiscards;                                // 8 bytes
        public ulong InErrors;                                  // 8 bytes
        public ulong InUnknownProtos;                           // 8 bytes
        public ulong InUcastOctets;                             // 8 bytes
        public ulong InMulticastOctets;                         // 8 bytes
        public ulong InBroadcastOctets;                         // 8 bytes
        public ulong OutOctets;                                 // 8 bytes
        public ulong OutUcastPkts;                              // 8 bytes
        public ulong OutNUcastPkts;                             // 8 bytes
        public ulong OutDiscards;                               // 8 bytes
        public ulong OutErrors;                                 // 8 bytes
        public ulong OutUcastOctets;                            // 8 bytes
        public ulong OutMulticastOctets;                        // 8 bytes
        public ulong OutBroadcastOctets;                        // 8 bytes
        public ulong OutQLen;                                   // 8 bytes
    }

    // Real structure size: 1352 bytes
    private const int MIB_IF_ROW2_SIZE = 1352;

    public static List<MIB_IF_ROW2> GetIfTable2()
    {
        var result = new List<MIB_IF_ROW2>();
        IntPtr tablePtr = IntPtr.Zero;

        try
        {
            int ret = GetIfTable2(out tablePtr);
            if (ret != 0)
                return result;

            // First DWORD is the count
            int count = Marshal.ReadInt32(tablePtr);

            // Bounds validation: protect against corrupt or malicious data from native API
            const int MaxReasonableInterfaces = 1000;
            if (count < 0 || count > MaxReasonableInterfaces)
            {
                System.Diagnostics.Debug.WriteLine($"Warning: Invalid interface count {count} from IP Helper API, returning empty result");
                return result;
            }
            
            // Skip the count (8 bytes on x64 due to alignment before first MIB_IF_ROW2)
            IntPtr rowPtr = tablePtr + 8;

            // Use the real Windows structure size, not Marshal.SizeOf which may differ
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_IF_ROW2>(rowPtr + (i * MIB_IF_ROW2_SIZE));
                result.Add(row);
            }
        }
        finally
        {
            if (tablePtr != IntPtr.Zero)
                FreeMibTable(tablePtr);
        }

        return result;
    }
}

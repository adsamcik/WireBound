# Per-Address (IP/URL) Network Tracking Design

## Overview

This document outlines the architecture for implementing per-address network tracking in WireBound, building upon the existing platform abstraction pattern and integrating with the proposed elevated helper process design.

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           WireBound.Avalonia                                 │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │ ProcessPollingBackgroundService (separate from NetworkPolling)         │ │
│  │   └── calls IProcessNetworkService                                     │ │
│  │   └── aggregates ConnectionStats into AddressUsageRecords              │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                      │                                       │
│                    ┌─────────────────▼─────────────────┐                    │
│                    │   ProcessNetworkService           │                    │
│                    │   (implements IProcessNetworkService)                  │
│                    │   └── uses IProcessNetworkProviderFactory              │
│                    │   └── caches DNS reverse lookups                       │
│                    └───────────────────────────────────┘                    │
└─────────────────────────────────────────────────────────────────────────────┘
                                       │
           ┌───────────────────────────▼───────────────────────────┐
           │          WireBound.Platform.Abstract                   │
           │  ┌──────────────────────────────────────────────────┐ │
           │  │  IProcessNetworkProvider (low-level platform API) │ │
           │  │    - GetActiveConnections()                       │ │
           │  │    - GetConnectionStats() [with byte counters]    │ │
           │  │  IProcessNetworkProviderFactory (elevation switch) │ │
           │  │  IHelperConnection (IPC abstraction)               │ │
           │  │  IDnsResolverService (hostname caching)            │ │
           │  └──────────────────────────────────────────────────┘ │
           │                                                       │
           │  Models:                                              │
           │  ┌──────────────────────────────────────────────────┐ │
           │  │  ConnectionInfo (IP, port, PID, protocol)        │ │
           │  │  ConnectionStats (bytes sent/recv per connection)│ │
           │  │  AddressUsageRecord (aggregated per IP/hostname) │ │
           │  └──────────────────────────────────────────────────┘ │
           └───────────────────────────────────────────────────────┘
                                       │
           ┌───────────────────────────▼───────────────────────────┐
           │              WireBound.IPC (new)                       │
           │  • MessagePack DTOs                                    │
           │  • Named Pipe transport (Windows)                      │
           │  • Unix Domain Socket transport (Linux)                │
           │  • Mutual authentication protocol                      │
           │                                                        │
           │  Messages:                                             │
           │  ┌──────────────────────────────────────────────────┐ │
           │  │  ConnectionStatsRequest / ConnectionStatsResponse│ │
           │  │  ProcessStatsRequest / ProcessStatsResponse      │ │
           │  │  AuthenticateRequest / AuthenticateResponse      │ │
           │  │  HeartbeatRequest / HeartbeatResponse            │ │
           │  └──────────────────────────────────────────────────┘ │
           └───────────────────────────────────────────────────────┘
                                       │
    ┌──────────────────────────────────┼──────────────────────────────────┐
    │                                  │                                  │
    ▼                                  ▼                                  ▼
┌─────────────────────────┐ ┌─────────────────────────┐ ┌─────────────────────┐
│ Platform.Windows        │ │ Platform.Linux          │ │ Platform.Stub       │
│                         │ │                         │ │                     │
│ Non-elevated:           │ │ Non-elevated:           │ │ • Simulated data    │
│ • GetExtendedTcpTable   │ │ • /proc/net/tcp[6]      │ │ • Test scenarios    │
│ • GetExtendedUdpTable   │ │ • /proc/net/udp[6]      │ │                     │
│ (connections only)      │ │ (connections only)      │ │                     │
│                         │ │                         │ │                     │
│ Elevated (via Helper):  │ │ Elevated (via Helper):  │ │                     │
│ • ETW Microsoft-Windows │ │ • eBPF tcp_sendmsg/     │ │                     │
│   -TCPIP provider       │ │   tcp_recvmsg probes    │ │                     │
│ • Per-connection bytes  │ │ • Per-connection bytes  │ │                     │
│ • Kernel aggregation    │ │ • BPF map aggregation   │ │                     │
└───────────┬─────────────┘ └───────────┬─────────────┘ └─────────────────────┘
            │                           │
            └─────────────┬─────────────┘
                          ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        WireBound.Helper (elevated)                           │
│                                                                              │
│  Security:                                                                   │
│  • Multi-factor auth (PID + timestamp + HMAC signature)                     │
│  • Max 8-hour session lifetime                                              │
│  • Rate limiting (100 req/sec per client)                                   │
│  • Parent process validation                                                │
│                                                                              │
│  Data Collection:                                                            │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │  Windows: ETW Session                    Linux: eBPF Program            │ │
│  │  ┌─────────────────────────────┐        ┌─────────────────────────────┐ │ │
│  │  │ Microsoft-Windows-TCPIP    │        │ kprobe: tcp_sendmsg         │ │ │
│  │  │ • TcpDataTransferSend      │        │ kprobe: tcp_recvmsg         │ │ │
│  │  │ • TcpDataTransferReceive   │        │ kprobe: udp_sendmsg         │ │ │
│  │  │ • UdpDatagramSend          │        │ kprobe: udp_recvmsg         │ │ │
│  │  │ • UdpDatagramReceive       │        │                             │ │ │
│  │  │                            │        │ BPF_HASH(conn_stats,        │ │ │
│  │  │ Aggregates by:             │        │   conn_key, conn_data)      │ │ │
│  │  │ {src_ip, dst_ip, dst_port, │        │                             │ │ │
│  │  │  protocol, pid}            │        │ Aggregates in kernel-space  │ │ │
│  │  └─────────────────────────────┘        └─────────────────────────────┘ │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
│                                                                              │
│  Output: Aggregated ConnectionStats (polled by main app via IPC)            │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Core Models (WireBound.Core/Models)

### ConnectionInfo.cs
```csharp
namespace WireBound.Core.Models;

/// <summary>
/// Represents an active network connection (from connection table).
/// Available without elevation.
/// </summary>
public class ConnectionInfo
{
    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public int ProcessId { get; set; }
    public string Protocol { get; set; } = "TCP"; // TCP or UDP
    public ConnectionState State { get; set; }
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Unique key for this connection tuple
    /// </summary>
    public string ConnectionKey => $"{Protocol}:{LocalAddress}:{LocalPort}->{RemoteAddress}:{RemotePort}";
}

public enum ConnectionState
{
    Unknown,
    Closed,
    Listen,
    SynSent,
    SynReceived,
    Established,
    FinWait1,
    FinWait2,
    CloseWait,
    Closing,
    LastAck,
    TimeWait,
    DeleteTcb
}
```

### ConnectionStats.cs
```csharp
namespace WireBound.Core.Models;

/// <summary>
/// Network statistics for a specific connection.
/// Byte counters require elevated helper process.
/// </summary>
public class ConnectionStats
{
    public string LocalAddress { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string RemoteAddress { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string Protocol { get; set; } = "TCP";
    public ConnectionState State { get; set; }
    
    // Resolved hostname (cached)
    public string? ResolvedHostname { get; set; }
    
    // Byte counters (requires elevation)
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    
    // Speed tracking
    public long SendSpeedBps { get; set; }
    public long ReceiveSpeedBps { get; set; }
    
    // Timing
    public DateTime FirstSeen { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    
    public long TotalBytes => BytesSent + BytesReceived;
    public long TotalSpeedBps => SendSpeedBps + ReceiveSpeedBps;
    
    public string ConnectionKey => $"{Protocol}:{LocalAddress}:{LocalPort}->{RemoteAddress}:{RemotePort}";
}
```

### AddressUsageRecord.cs
```csharp
namespace WireBound.Core.Models;

/// <summary>
/// Aggregated network usage for a specific remote address.
/// Used for persistence and historical tracking.
/// </summary>
public class AddressUsageRecord
{
    public int Id { get; set; }
    
    /// <summary>
    /// Remote IP address
    /// </summary>
    public string RemoteAddress { get; set; } = string.Empty;
    
    /// <summary>
    /// Resolved hostname (if available)
    /// </summary>
    public string? Hostname { get; set; }
    
    /// <summary>
    /// Most common destination port (e.g., 443 for HTTPS)
    /// </summary>
    public int PrimaryPort { get; set; }
    
    /// <summary>
    /// Protocol (TCP/UDP)
    /// </summary>
    public string Protocol { get; set; } = "TCP";
    
    /// <summary>
    /// Timestamp for this record (hour or day granularity)
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Granularity level
    /// </summary>
    public UsageGranularity Granularity { get; set; }
    
    /// <summary>
    /// Total bytes sent to this address
    /// </summary>
    public long BytesSent { get; set; }
    
    /// <summary>
    /// Total bytes received from this address
    /// </summary>
    public long BytesReceived { get; set; }
    
    /// <summary>
    /// Number of connections to this address in this period
    /// </summary>
    public int ConnectionCount { get; set; }
    
    /// <summary>
    /// Optional: Link to the process that generated this traffic
    /// </summary>
    public string? AppIdentifier { get; set; }
    
    public long TotalBytes => BytesSent + BytesReceived;
}
```

---

## Platform Abstractions (WireBound.Platform.Abstract/Services)

### IProcessNetworkProvider.cs
```csharp
namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Low-level platform API for network connection and process monitoring.
/// Implementations differ by platform and elevation level.
/// </summary>
public interface IProcessNetworkProvider : IDisposable
{
    /// <summary>
    /// Whether this provider requires elevated privileges
    /// </summary>
    bool RequiresElevation { get; }
    
    /// <summary>
    /// Whether byte-level tracking is available (requires elevation)
    /// </summary>
    bool SupportsbyteTracking { get; }
    
    /// <summary>
    /// Get all active connections (available without elevation)
    /// </summary>
    IReadOnlyList<ConnectionInfo> GetActiveConnections();
    
    /// <summary>
    /// Get connection statistics with byte counters (requires elevation)
    /// </summary>
    IReadOnlyList<ConnectionStats> GetConnectionStats();
    
    /// <summary>
    /// Get per-process aggregated statistics
    /// </summary>
    IReadOnlyList<ProcessNetworkStats> GetProcessStats();
    
    /// <summary>
    /// Start the provider
    /// </summary>
    Task<bool> StartAsync();
    
    /// <summary>
    /// Stop the provider
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// Whether the provider is currently running
    /// </summary>
    bool IsRunning { get; }
}
```

### IProcessNetworkProviderFactory.cs
```csharp
namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Factory for creating the appropriate network provider based on elevation state.
/// </summary>
public interface IProcessNetworkProviderFactory
{
    /// <summary>
    /// Create a provider for non-elevated mode (connection enumeration only)
    /// </summary>
    IProcessNetworkProvider CreateBasicProvider();
    
    /// <summary>
    /// Create a provider that connects to the elevated helper process
    /// </summary>
    IProcessNetworkProvider CreateElevatedProvider(IHelperConnection connection);
    
    /// <summary>
    /// Whether the elevated helper is available
    /// </summary>
    bool IsHelperAvailable { get; }
    
    /// <summary>
    /// Attempt to launch the elevated helper process
    /// </summary>
    Task<IHelperConnection?> LaunchHelperAsync();
}
```

### IHelperConnection.cs
```csharp
namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Abstraction for IPC connection to the elevated helper process.
/// </summary>
public interface IHelperConnection : IAsyncDisposable
{
    /// <summary>
    /// Whether the connection is currently active
    /// </summary>
    bool IsConnected { get; }
    
    /// <summary>
    /// Unique session ID for this connection
    /// </summary>
    string SessionId { get; }
    
    /// <summary>
    /// Connect to the helper process
    /// </summary>
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Disconnect from the helper process
    /// </summary>
    Task DisconnectAsync();
    
    /// <summary>
    /// Request connection statistics from the helper
    /// </summary>
    Task<IReadOnlyList<ConnectionStats>> RequestConnectionStatsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Request process statistics from the helper
    /// </summary>
    Task<IReadOnlyList<ProcessNetworkStats>> RequestProcessStatsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send a heartbeat to keep the session alive
    /// </summary>
    Task<bool> SendHeartbeatAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Raised when the connection is lost
    /// </summary>
    event EventHandler? ConnectionLost;
}
```

### IDnsResolverService.cs
```csharp
namespace WireBound.Platform.Abstract.Services;

/// <summary>
/// Service for resolving IP addresses to hostnames with caching.
/// </summary>
public interface IDnsResolverService
{
    /// <summary>
    /// Resolve an IP address to a hostname (cached)
    /// </summary>
    Task<string?> ResolveAsync(string ipAddress, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get a cached hostname if available (non-blocking)
    /// </summary>
    string? GetCached(string ipAddress);
    
    /// <summary>
    /// Clear the DNS cache
    /// </summary>
    void ClearCache();
    
    /// <summary>
    /// Number of entries in cache
    /// </summary>
    int CacheSize { get; }
}
```

---

## IPC Layer (WireBound.IPC)

### Message DTOs (MessagePack)

```csharp
using MessagePack;

namespace WireBound.IPC.Messages;

[MessagePackObject]
public class AuthenticateRequest
{
    [Key(0)] public int ClientPid { get; set; }
    [Key(1)] public long Timestamp { get; set; }
    [Key(2)] public byte[] Signature { get; set; } = [];
    [Key(3)] public string ClientPath { get; set; } = string.Empty;
}

[MessagePackObject]
public class AuthenticateResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string SessionId { get; set; } = string.Empty;
    [Key(2)] public DateTime ExpiresAt { get; set; }
    [Key(3)] public string? ErrorMessage { get; set; }
}

[MessagePackObject]
public class ConnectionStatsRequest
{
    [Key(0)] public string SessionId { get; set; } = string.Empty;
}

[MessagePackObject]
public class ConnectionStatsResponse
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public ConnectionStatsDto[] Connections { get; set; } = [];
    [Key(2)] public DateTime Timestamp { get; set; }
}

[MessagePackObject]
public class ConnectionStatsDto
{
    [Key(0)] public string LocalAddress { get; set; } = string.Empty;
    [Key(1)] public ushort LocalPort { get; set; }
    [Key(2)] public string RemoteAddress { get; set; } = string.Empty;
    [Key(3)] public ushort RemotePort { get; set; }
    [Key(4)] public int ProcessId { get; set; }
    [Key(5)] public byte Protocol { get; set; } // 6=TCP, 17=UDP
    [Key(6)] public long BytesSent { get; set; }
    [Key(7)] public long BytesReceived { get; set; }
}
```

### Transport Abstraction

```csharp
namespace WireBound.IPC.Transport;

/// <summary>
/// Platform-specific IPC transport
/// </summary>
public interface IIpcTransport : IAsyncDisposable
{
    Task ConnectAsync(string endpoint, CancellationToken cancellationToken = default);
    Task<byte[]> SendAndReceiveAsync(byte[] data, CancellationToken cancellationToken = default);
    bool IsConnected { get; }
    event EventHandler? Disconnected;
}

// Implementations:
// - NamedPipeTransport (Windows)
// - UnixDomainSocketTransport (Linux)
```

---

## Windows Implementation Details

### Non-Elevated: Connection Enumeration

Use `GetExtendedTcpTable` and `GetExtendedUdpTable` P/Invoke:

```csharp
// WireBound.Platform.Windows/Services/WindowsConnectionEnumerator.cs

[DllImport("iphlpapi.dll", SetLastError = true)]
static extern uint GetExtendedTcpTable(
    IntPtr pTcpTable,
    ref int pdwSize,
    bool bOrder,
    int ulAf,
    TCP_TABLE_CLASS tableClass,
    uint reserved);

// Returns: MIB_TCPROW_OWNER_PID[] with:
// - dwLocalAddr, dwLocalPort
// - dwRemoteAddr, dwRemotePort
// - dwOwningPid
// - dwState
```

### Elevated: ETW Provider

```csharp
// WireBound.Helper.Windows/EtwNetworkTracer.cs

// Provider: Microsoft-Windows-TCPIP
// GUID: {2F07E2EE-15DB-40F1-90EF-9D7BA282188A}

// Events of interest:
// - TcpDataTransferSend (ID: 1013)
// - TcpDataTransferReceive (ID: 1014)  
// - UdpDatagramSend (ID: 1015)
// - UdpDatagramReceive (ID: 1016)

// Each event contains:
// - Process ID
// - Local/Remote addresses
// - Bytes transferred
```

---

## Linux Implementation Details

### Non-Elevated: /proc/net Parsing

```csharp
// WireBound.Platform.Linux/Services/LinuxConnectionEnumerator.cs

// Parse /proc/net/tcp, /proc/net/tcp6, /proc/net/udp, /proc/net/udp6
// Format: sl local_address rem_address st tx_queue:rx_queue ... uid inode

// Need to correlate inode -> PID via /proc/[pid]/fd/
```

### Elevated: eBPF Probes

```c
// WireBound.Helper.Linux/ebpf/conn_tracker.bpf.c

struct conn_key {
    __u32 saddr;
    __u32 daddr;
    __u16 sport;
    __u16 dport;
    __u32 pid;
    __u8 protocol;
};

struct conn_data {
    __u64 bytes_sent;
    __u64 bytes_recv;
    __u64 last_activity;
};

BPF_HASH(connections, struct conn_key, struct conn_data, 65536);

SEC("kprobe/tcp_sendmsg")
int trace_tcp_send(struct pt_regs *ctx) {
    // Extract connection info and bytes
    // Update connections map
}
```

---

## Implementation Phases

### Phase 1: Connection Enumeration (No Elevation) ✅ Can start now

1. Add `ConnectionInfo` and `ConnectionState` models to Core
2. Add `IProcessNetworkProvider` interface to Platform.Abstract
3. Implement `WindowsConnectionEnumerator` using P/Invoke
4. Implement `LinuxConnectionEnumerator` parsing /proc/net
5. Add `StubConnectionEnumerator` with test data
6. Create `ConnectionsView.axaml` UI

**Deliverables:**
- See active TCP/UDP connections
- See which process owns each connection
- See remote IP addresses and ports
- No byte counters (grayed out)

### Phase 2: DNS Resolution & Caching

1. Implement `DnsResolverService` with LRU cache
2. Async background resolution
3. Persist hostname cache to SQLite
4. Display hostnames in UI

### Phase 3: IPC Infrastructure

1. Create `WireBound.IPC` project
2. Define MessagePack DTOs
3. Implement `NamedPipeTransport` (Windows)
4. Implement `UnixDomainSocketTransport` (Linux)
5. Add authentication protocol

### Phase 4: Helper Process

1. Create `WireBound.Helper` project
2. Implement authentication/session management
3. Windows: ETW tracing
4. Linux: eBPF program loading
5. Kernel-side aggregation

### Phase 5: Elevated Integration

1. `IProcessNetworkProviderFactory` implementation
2. Helper launch with UAC/pkexec
3. Byte counter integration
4. Fallback to basic mode on failure

### Phase 6: Persistence & History

1. `AddressUsageRecord` EF Core entity
2. Hourly/daily aggregation
3. History view for per-address usage
4. Export functionality

---

## Security Considerations

### Helper Process Authentication

```
1. Main app generates: PID + Timestamp + HMAC-SHA256(secret)
2. Helper validates:
   - PID matches expected parent process
   - Timestamp within 30 seconds
   - Signature valid
   - Client executable path is trusted
3. Helper issues session token (8-hour max)
4. All subsequent requests require valid session token
```

### Rate Limiting

```
- Max 100 requests/second per client
- Max 10 concurrent sessions
- Automatic session cleanup after 8 hours
```

---

## Performance Targets

| Metric | Target | Notes |
|--------|--------|-------|
| Connection enumeration | < 10ms | Polling every 1s |
| ETW event processing | < 1% CPU | Kernel aggregation |
| Memory (helper) | < 50MB | Bounded hash maps |
| IPC latency | < 5ms | Local socket/pipe |
| DNS cache hit rate | > 90% | LRU with 10K entries |

---

## File Structure

```
src/
├── WireBound.Core/
│   └── Models/
│       ├── ConnectionInfo.cs      (Phase 1)
│       ├── ConnectionStats.cs     (Phase 1)
│       └── AddressUsageRecord.cs  (Phase 6)
│
├── WireBound.Platform.Abstract/
│   └── Services/
│       ├── IProcessNetworkProvider.cs      (Phase 1)
│       ├── IProcessNetworkProviderFactory.cs (Phase 5)
│       ├── IHelperConnection.cs            (Phase 3)
│       └── IDnsResolverService.cs          (Phase 2)
│
├── WireBound.Platform.Windows/
│   └── Services/
│       ├── WindowsConnectionEnumerator.cs  (Phase 1)
│       ├── WindowsHelperConnection.cs      (Phase 5)
│       └── WindowsProcessNetworkProviderFactory.cs (Phase 5)
│
├── WireBound.Platform.Linux/
│   └── Services/
│       ├── LinuxConnectionEnumerator.cs    (Phase 1)
│       ├── LinuxHelperConnection.cs        (Phase 5)
│       └── LinuxProcessNetworkProviderFactory.cs (Phase 5)
│
├── WireBound.IPC/                          (Phase 3)
│   ├── Messages/
│   │   └── *.cs (MessagePack DTOs)
│   ├── Transport/
│   │   ├── IIpcTransport.cs
│   │   ├── NamedPipeTransport.cs
│   │   └── UnixDomainSocketTransport.cs
│   └── WireBound.IPC.csproj
│
├── WireBound.Helper/                       (Phase 4)
│   ├── Windows/
│   │   └── EtwNetworkTracer.cs
│   ├── Linux/
│   │   ├── EbpfLoader.cs
│   │   └── ebpf/conn_tracker.bpf.c
│   ├── Security/
│   │   ├── SessionManager.cs
│   │   └── RateLimiter.cs
│   └── WireBound.Helper.csproj
│
└── WireBound.Avalonia/
    ├── Services/
    │   ├── ProcessNetworkService.cs        (Phase 1)
    │   ├── ProcessPollingBackgroundService.cs (Phase 1)
    │   └── DnsResolverService.cs           (Phase 2)
    ├── ViewModels/
    │   └── ConnectionsViewModel.cs         (Phase 1)
    └── Views/
        └── ConnectionsView.axaml           (Phase 1)
```

---

## Dependencies

### Phase 1 (No new packages)
- System.Net.NetworkInformation (built-in)

### Phase 2
- No additional packages (System.Net.Dns)

### Phase 3
- MessagePack (NuGet)
- System.IO.Pipelines (built-in)

### Phase 4 (Windows)
- Microsoft.Diagnostics.Tracing.TraceEvent (NuGet)

### Phase 4 (Linux)
- libbpf (native, loaded via P/Invoke)
- Pre-compiled eBPF bytecode

---

## Next Steps

1. **Immediate**: Create `ConnectionInfo` model and `IProcessNetworkProvider` interface
2. **Week 1**: Implement Windows/Linux connection enumerators
3. **Week 2**: Add ConnectionsView UI
4. **Week 3**: DNS resolver with caching
5. **Month 2**: IPC layer and helper process scaffold
6. **Month 3**: ETW/eBPF integration

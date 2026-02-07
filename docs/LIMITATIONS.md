# Known Limitations

This document describes current limitations in WireBound and provides transparency about feature availability. We believe in honest documentation—knowing what works and what doesn't helps you make informed decisions.

---

## Per-Process Network Byte Tracking

### Current Status: **Partial Implementation**

WireBound can show **which processes have active network connections**, but **per-connection byte counting is limited** until the elevated helper process is implemented.

### What Works Now

| Feature | Status | Notes |
|---------|--------|-------|
| List active connections | ✅ Working | Shows all TCP/UDP connections |
| Process names and PIDs | ✅ Working | Identifies which app owns each connection |
| Connection endpoints | ✅ Working | Local and remote IP:port pairs |
| Connection state | ✅ Working | ESTABLISHED, TIME_WAIT, etc. |
| DNS hostname resolution | ✅ Working | Reverse lookup with caching |
| Session total usage | ✅ Working | Total bytes since app started |

### What's Limited

| Feature | Status | Why |
|---------|--------|-----|
| Per-connection bytes sent/received | ⚠️ Estimated | Requires elevated helper |
| Per-connection speed (bytes/sec) | ⚠️ Estimated | Requires elevated helper |
| Per-app historical usage | ⚠️ Estimated | Aggregated from estimated values |

### Why This Limitation Exists

Accurate per-connection byte tracking requires low-level access to network statistics:

- **Windows**: ETW (Event Tracing for Windows) via elevated helper process
- **Linux**: eBPF or `/proc/net/tcp` with root access

Without elevation, WireBound uses **estimation algorithms** that distribute total adapter traffic proportionally across active connections. These estimates are marked in the UI.

### What the Elevated Helper Will Provide

The planned helper process architecture will enable:

1. **Windows Helper** (`WireBound.Helper.exe`)
   - Runs as a Windows service with administrator privileges
   - Uses ETW to capture per-socket byte transfers
   - Communicates via named pipe (`\\.\pipe\WireBound.Helper`)

2. **Linux Helper** (`wirebound-helper`)
   - Runs as root via systemd or polkit
   - Uses eBPF for efficient packet accounting
   - Communicates via Unix domain socket (`/run/wirebound/helper.sock`)

### How to Know If Byte Tracking Is Limited

In the Connections and Applications views, look for:

- The `IsByteTrackingLimited` indicator in the UI
- Byte values displayed with an estimation marker
- The "Requires Elevation" prompt (when helper is not running)

### Roadmap

The elevated helper is planned but not yet scheduled. Track progress:

- Design document: [DESIGN_PER_ADDRESS_TRACKING.md](./DESIGN_PER_ADDRESS_TRACKING.md)
- Helper IPC design: Planned for `WireBound.IPC` project

---

## Platform-Specific Limitations

### Windows

| Feature | Availability | Notes |
|---------|--------------|-------|
| WiFi signal strength | ✅ Full | Uses netsh and WlanApi |
| Per-process connections | ✅ Full | Basic info via GetExtendedTcpTable |
| Per-process byte counters | ⚠️ Requires helper | ETW access needs elevation |
| Startup on login | ✅ Full | Registry-based |

### Linux

| Feature | Availability | Notes |
|---------|--------------|-------|
| WiFi signal strength | ✅ Full | Uses nmcli |
| Per-process connections | ✅ Full | Parses `ss` output |
| Per-process byte counters | ⚠️ Requires helper | eBPF needs root |
| Startup on login | ✅ Full | XDG autostart |

---

## Data Persistence Limitations

- **Historical data**: Stored locally in SQLite—no cloud sync
- **Export format**: CSV export available in Settings → Data Management
- **Backup**: One-click backup available in Settings → Data Management

---

## Contributing

If you'd like to help implement the elevated helper or other missing features, see:

- [DESIGN_PER_ADDRESS_TRACKING.md](./DESIGN_PER_ADDRESS_TRACKING.md) for architecture
- [Contributing guidelines](../CONTRIBUTING.md) (if available)

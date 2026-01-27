# Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                             User Interface Layer                             │
│  WireBound.Avalonia                                                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │    Views    │  │  ViewModels │  │  Controls   │  │     Converters      │ │
│  │  (AXAML)    │  │  (MVVM+DI)  │  │  (Custom)   │  │  (Value Converters) │ │
│  └─────────────┘  └─────────────┘  └─────────────┘  └─────────────────────┘ │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │                        UI Services                                       ││
│  │  NavigationService, ViewFactory, TrayIconService, LocalizationService   ││
│  └─────────────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │ DI / Service Resolution
┌──────────────────────────────────────┼──────────────────────────────────────┐
│                               Core Layer                                     │
│  WireBound.Core                                                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │   Models    │  │  Service    │  │    Data     │  │      Helpers        │ │
│  │ NetworkStats│  │ Interfaces  │  │ DbContext   │  │ ByteFormatter       │ │
│  │ AppSettings │  │ INetwork... │  │ Migrations  │  │ ChartColors         │ │
│  │ SystemStats │  │ ISystem...  │  │             │  │ CircularBuffer      │ │
│  └─────────────┘  └─────────────┘  └─────────────┘  └─────────────────────┘ │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │ Interface Implementation
┌──────────────────────────────────────┼──────────────────────────────────────┐
│                           Platform Abstraction                               │
│  WireBound.Platform.Abstract                                                 │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  IPlatformServices, ICpuInfoProvider, IMemoryInfoProvider,              ││
│  │  IProcessNetworkProvider, IStartupService, IWiFiInfoProvider,           ││
│  │  IElevationService, IDnsResolverService                                 ││
│  └─────────────────────────────────────────────────────────────────────────┘│
└──────────────────────────────────────┬──────────────────────────────────────┘
           ┌───────────────────────────┼───────────────────────────┐
           │                           │                           │
           ▼                           ▼                           ▼
┌────────────────────────┐  ┌────────────────────────┐  ┌────────────────────────┐
│  WireBound.Platform.   │  │  WireBound.Platform.   │  │  WireBound.Platform.   │
│       Windows          │  │        Linux           │  │        Stub            │
│                        │  │                        │  │                        │
│  WindowsCpuInfoProvider│  │  LinuxCpuInfoProvider  │  │  StubCpuInfoProvider   │
│  WindowsMemoryInfo...  │  │  LinuxMemoryInfo...    │  │  StubMemoryInfo...     │
│  WindowsStartupService │  │  LinuxStartupService   │  │  StubStartupService    │
│  WindowsWiFiInfoProvider│ │  LinuxWiFiInfoProvider │  │  StubWiFiInfoProvider  │
│                        │  │                        │  │                        │
│  Uses: WMI, PerfCounter│  │  Uses: /proc, nmcli    │  │  Returns: Safe defaults│
│  Registry, Native APIs │  │  ss, iw commands       │  │                        │
└────────────────────────┘  └────────────────────────┘  └────────────────────────┘
```

## Component Map

### WireBound.Avalonia (UI Application)

#### Views
| View | Purpose | Key Dependencies |
|------|---------|------------------|
| `MainWindow` | Application shell, navigation rail, content host | MainViewModel |
| `OverviewView` | Quick dashboard with network + system metrics | OverviewViewModel |
| `ChartsView` | Real-time interactive chart with time range selection | ChartsViewModel |
| `SystemView` | Dedicated CPU/RAM monitoring with gauges | SystemViewModel |
| `ApplicationsView` | Per-application network usage tracking | ApplicationsViewModel |
| `ConnectionsView` | Active network connections list | ConnectionsViewModel |
| `InsightsView` | Tabbed analytics: usage, trends, correlations | InsightsViewModel |
| `SettingsView` | App configuration and preferences | SettingsViewModel |

#### ViewModels
| ViewModel | Responsibilities | Services Used |
|-----------|------------------|---------------|
| `MainViewModel` | Navigation state, app version | INavigationService, IViewFactory |
| `OverviewViewModel` | Network + system stats aggregation | INetworkMonitorService, ISystemMonitorService, IDataPersistenceService |
| `ChartsViewModel` | Live chart data management | INetworkPollingBackgroundService, ISpeedSnapshotRepository |
| `SystemViewModel` | CPU/RAM display and history | ISystemMonitorService, ISystemHistoryService |
| `ApplicationsViewModel` | Per-app bandwidth totals | IProcessNetworkService |
| `ConnectionsViewModel` | Active TCP/UDP connections | IProcessNetworkService, IDnsResolverService |
| `InsightsViewModel` | Historical analytics, trends | IDataPersistenceService, ISystemHistoryService |
| `SettingsViewModel` | User preferences | ISettingsRepository |

#### Custom Controls
| Control | Purpose |
|---------|---------|
| `CircularGauge` | Radial progress indicator for CPU/RAM percentage |
| `MiniSparkline` | Inline trend chart (60-second history) |
| `SystemHealthStrip` | Header bar with CPU/RAM/GPU at-a-glance |

### WireBound.Core (Shared Library)

#### Models
| Model | Purpose |
|-------|---------|
| `NetworkStats` | Real-time speed (Bps) and cumulative bytes |
| `NetworkAdapter` | Adapter info: name, type, MAC, speed |
| `HourlyUsage` / `DailyUsage` | Historical network data aggregations |
| `HourlySystemStats` / `DailySystemStats` | Historical CPU/RAM aggregations |
| `AppSettings` | User preferences (polling interval, themes, etc.) |
| `AppUsageRecord` | Per-application bandwidth record |
| `SpeedSnapshot` | Point-in-time speed for chart history |
| `CpuStats` / `MemoryStats` / `SystemStats` | System resource measurements |

#### Service Interfaces
| Interface | Purpose |
|-----------|---------|
| `INetworkMonitorService` | Poll network adapters, calculate speeds |
| `IDataPersistenceService` | High-level data save/load |
| `INetworkUsageRepository` | Hourly/daily usage CRUD |
| `IAppUsageRepository` | Per-app usage CRUD |
| `ISettingsRepository` | App settings CRUD |
| `ISpeedSnapshotRepository` | Speed history for charts |
| `ISystemMonitorService` | Current CPU/RAM stats |
| `ISystemHistoryService` | Historical system stats management |
| `INavigationService` | View navigation events |
| `ILocalizationService` | String translations |
| `ITrayIconService` | System tray functionality |

#### Helpers
| Helper | Purpose |
|--------|---------|
| `ByteFormatter` | Format bytes to human-readable (KB, MB, GB) |
| `ChartColors` | Consistent chart color palette |
| `CircularBuffer<T>` | O(1) fixed-size buffer for chart data |
| `LttbDownsampler` | Largest-Triangle-Three-Buckets downsampling |
| `AdaptiveThresholdCalculator` | Dynamic threshold calculation |
| `TrendIndicatorCalculator` | Trend direction detection |

### WireBound.Platform.Abstract (Interfaces)

| Interface | Purpose | Windows Impl | Linux Impl |
|-----------|---------|--------------|------------|
| `IPlatformServices` | Factory for registering all platform services | WindowsPlatformServices | LinuxPlatformServices |
| `ICpuInfoProvider` | Get CPU usage percentage | PerformanceCounter | /proc/stat |
| `IMemoryInfoProvider` | Get RAM usage | GlobalMemoryStatusEx | /proc/meminfo |
| `IProcessNetworkProvider` | Per-process network stats | GetExtendedTcpTable | /proc/net/* |
| `IStartupService` | Register/unregister startup | Registry | XDG autostart |
| `IWiFiInfoProvider` | WiFi SSID, signal strength | ManagedNativeWifi | nmcli/iw |
| `IElevationService` | Admin/root privilege check | UAC | pkexec |

## Data Flow

### Network Monitoring Flow

```
NetworkPollingBackgroundService (Timer: 1s)
       │
       ▼
INetworkMonitorService.GetCurrentStats()
       │
       ├──► CrossPlatformNetworkMonitorService
       │         │
       │         ▼
       │    System.Net.NetworkInterface.GetIPStatistics()
       │         │
       │         ▼
       │    Calculate delta from previous reading
       │
       ▼
NetworkPollingBackgroundService raises StatsUpdated event
       │
       ├──► OverviewViewModel.OnStatsUpdated() ──► Update UI bindings
       ├──► ChartsViewModel.OnStatsUpdated() ──► Add to chart series
       └──► Periodic: IDataPersistenceService.SaveStatsAsync()
                                │
                                ▼
                        WireBoundDbContext.SaveChangesAsync()
```

### Navigation Flow

```
User clicks nav item
       │
       ▼
MainViewModel.SelectedNavigationItem setter
       │
       ▼
INavigationService.NavigateTo(route)
       │
       ▼
NavigationChanged event raised
       │
       ▼
MainViewModel.OnNavigationChanged()
       │
       ▼
IViewFactory.CreateView(route)
       │
       ▼
ServiceProvider.GetRequiredService<TView>()
       │
       ▼
MainViewModel.CurrentView = new view
       │
       ▼
ContentControl binding updates UI
```

### Platform Service Registration

```
App.ConfigureServices()
       │
       ├──► StubPlatformServices.Register() ── Always first (defaults)
       │
       ├──► if Windows: WindowsPlatformServices.Register() ── Overrides stubs
       │
       └──► if Linux: LinuxPlatformServices.Register() ── Overrides stubs
```

## External Integrations

| Service | Purpose | Configuration |
|---------|---------|---------------|
| SQLite | Local persistence | `LocalApplicationData/WireBound/wirebound.db` |
| Serilog | Structured logging | Configured in `Program.cs` |
| LiveChartsCore | Charting library | Configured in `App.Initialize()` |

## Database Schema

### Core Tables

| Table | Columns | Purpose |
|-------|---------|---------|
| `HourlyUsages` | Id, Date, Hour, BytesSent, BytesReceived | Hourly network aggregates |
| `DailyUsages` | Id, Date, BytesSent, BytesReceived | Daily network aggregates |
| `HourlySystemStats` | Id, Date, Hour, AvgCpuPercent, AvgMemoryPercent, MaxCpuPercent, MaxMemoryPercent | Hourly system stats |
| `DailySystemStats` | Id, Date, AvgCpuPercent, AvgMemoryPercent, MaxCpuPercent, MaxMemoryPercent | Daily system stats |
| `AppSettings` | Id, PollingIntervalMs, MinimizeToTray, StartMinimized, ... | User preferences |
| `AppUsageRecords` | Id, ProcessName, Date, BytesSent, BytesReceived | Per-app bandwidth |
| `SpeedSnapshots` | Id, Timestamp, DownloadBps, UploadBps | Speed history for charts |

<!--
context-init:version: 3.0.0
context-init:generated: 2026-02-07T14:21:00Z
context-init:mode: full-init
-->

# Architecture

## System Overview

<!-- context-init:managed -->
```text
┌─────────────────────────────────────────────────────────────────────────────┐
│                             User Interface Layer                             │
│  WireBound.Avalonia                                                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │    Views    │  │  ViewModels │  │  Controls   │  │     Converters      │ │
│  │  (AXAML)    │  │  (MVVM+DI)  │  │  (Custom)   │  │  (Value Converters) │ │
│  └─────────────┘  └─────────────┘  └─────────────┘  └─────────────────────┘ │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │                        UI Services                                      │ │
│  │  NavigationService, ViewFactory, TrayIconService, LocalizationService   │ │
│  │  NetworkPollingBackgroundService, SystemMonitorService, DnsResolver      │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
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
│  │ SpeedUnit   │  │ IProcess... │  │             │  │ LttbDownsampler     │ │
│  └─────────────┘  └─────────────┘  └─────────────┘  └─────────────────────┘ │
└──────────────────────────────────────┬──────────────────────────────────────┘
                                       │ Interface Implementation
┌──────────────────────────────────────┼──────────────────────────────────────┐
│                           Platform Abstraction                               │
│  WireBound.Platform.Abstract                                                 │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  IPlatformServices, ICpuInfoProvider, IMemoryInfoProvider,              │ │
│  │  IProcessNetworkProvider, IProcessNetworkProviderFactory,               │ │
│  │  IStartupService, IWiFiInfoProvider, IElevationService,                 │ │
│  │  IDnsResolverService, IHelperConnection, IHelperProcessManager          │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │  Models: ProcessNetworkStats, ConnectionInfo, ConnectionStats,          │ │
│  │          CpuInfoData, MemoryInfoData                                    │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
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
│  WindowsProcessNetwork │  │  LinuxProcessNetwork   │  │  StubProcessNetwork    │
│  WindowsElevation...   │  │  LinuxElevation...     │  │  StubElevation...      │
│                        │  │                        │  │                        │
│  Uses: WMI, PerfCounter│  │  Uses: /proc, nmcli    │  │  Returns: Safe defaults│
│  Registry, Native APIs │  │  ss, iw commands       │  │                        │
└────────────────────────┘  └────────────────────────┘  └────────────────────────┘
```

## Component Map

<!-- context-init:managed -->

### WireBound.Avalonia (UI Application)

#### Views → ViewModels

| View | ViewModel | Purpose |
|------|-----------|---------|
| `MainWindow` | `MainViewModel` | App shell, navigation rail, content host |
| `OverviewView` | `OverviewViewModel` | Dashboard with network + system metrics |
| `ChartsView` | `ChartsViewModel` | Real-time interactive chart, time range selection |
| `SystemView` | `SystemViewModel` | CPU/RAM gauges and history |
| `ApplicationsView` | `ApplicationsViewModel` | Per-application network usage |
| `ConnectionsView` | `ConnectionsViewModel` | Active TCP/UDP connections |
| `InsightsView` | `InsightsViewModel` | Tabbed analytics: usage, trends, correlations |
| `SettingsView` | `SettingsViewModel` | App configuration and preferences |

#### ViewModel Dependencies

| ViewModel | Services Used |
|-----------|---------------|
| `MainViewModel` | INavigationService, IViewFactory |
| `OverviewViewModel` | INetworkMonitorService, ISystemMonitorService, IDataPersistenceService |
| `ChartsViewModel` | INetworkPollingBackgroundService, ISpeedSnapshotRepository |
| `SystemViewModel` | ISystemMonitorService, ISystemHistoryService |
| `ApplicationsViewModel` | IProcessNetworkService |
| `ConnectionsViewModel` | IProcessNetworkService, IDnsResolverService |
| `InsightsViewModel` | IDataPersistenceService, ISystemHistoryService |
| `SettingsViewModel` | ISettingsRepository |

#### Custom Controls

| Control | Purpose | Key Properties |
|---------|---------|----------------|
| `CircularGauge` | Radial progress for CPU/RAM percentage | Value, StrokeColor, Size |
| `MiniSparkline` | Inline 60-second trend chart | Data points collection |
| `SystemHealthStrip` | Header bar with CPU/RAM at-a-glance | System stats binding |

#### UI Services

| Service | Purpose |
|---------|---------|
| `NavigationService` | Route-based navigation with events |
| `ViewFactory` | Creates views from DI container |
| `CrossPlatformNetworkMonitorService` | Polls `NetworkInterface.GetIPStatistics()` |
| `NetworkPollingBackgroundService` | Timer-based polling (1s interval) |
| `DataPersistenceService` | Implements 5 repository interfaces |
| `SystemMonitorService` | Aggregates CPU/RAM from platform providers |
| `SystemHistoryService` | Manages historical system stats |
| `ProcessNetworkService` | Per-process bandwidth tracking |
| `DnsResolverService` | Async DNS resolution with caching |
| `WiFiInfoService` | WiFi SSID/signal from platform provider |
| `LocalizationService` | i18n string provider |
| `TrayIconService` | System tray integration |

#### Converters & Helpers

| Component | Purpose |
|-----------|---------|
| `SelectedRowConverter` | DataGrid row selection converter |
| `SpeedUnitConverter` | Speed unit display formatting |
| `ChartSeriesFactory` | Creates LiveCharts series objects |
| `ChartDataManager` | Manages chart data lifecycle |

### WireBound.Core (Shared Library)

#### Models (17)

| Model | Purpose |
|-------|---------|
| `NetworkStats` | Real-time speed (Bps) and cumulative bytes |
| `NetworkAdapter` | Adapter info: name, type, MAC, speed |
| `HourlyUsage` / `DailyUsage` | Historical network data aggregations |
| `HourlySystemStats` / `DailySystemStats` | Historical CPU/RAM aggregations |
| `AppSettings` | User preferences (polling interval, theme, etc.) |
| `AppUsageRecord` / `AddressUsageRecord` | Per-app and per-address bandwidth |
| `SpeedSnapshot` | Point-in-time speed for chart history |
| `SpeedUnit` | Speed display unit enumeration |
| `CpuStats` / `MemoryStats` / `SystemStats` | System resource measurements |
| `ConnectionInfo` / `ConnectionStats` | Active connection tracking |
| `TimeRangeOption` | Chart time range selector options |

#### Service Interfaces (14)

| Interface | Purpose |
|-----------|---------|
| `INetworkMonitorService` | Poll network adapters, calculate speeds |
| `INetworkPollingBackgroundService` | Background timer with StatsUpdated event |
| `IDataPersistenceService` | High-level data persistence |
| `INetworkUsageRepository` | Hourly/daily usage CRUD |
| `IAppUsageRepository` | Per-app usage CRUD |
| `ISettingsRepository` | App settings CRUD |
| `ISpeedSnapshotRepository` | Speed history for charts |
| `ISystemMonitorService` | Current CPU/RAM stats |
| `ISystemHistoryService` | Historical system stats management |
| `IProcessNetworkService` | Per-process network stats |
| `INavigationService` | View navigation events |
| `ILocalizationService` | i18n string provider |
| `ITrayIconService` | System tray functionality |
| `IWiFiInfoService` | WiFi connection info |

#### Helpers (6)

| Helper | Purpose |
|--------|---------|
| `ByteFormatter` | Format bytes → human-readable (KB, MB, GB) |
| `ChartColors` | Consistent chart color palette |
| `CircularBuffer<T>` | O(1) fixed-size ring buffer for chart data |
| `LttbDownsampler` | Largest-Triangle-Three-Buckets chart downsampling |
| `AdaptiveThresholdCalculator` | Dynamic threshold computation |
| `TrendIndicatorCalculator` | Trend direction detection (up/down/stable) |

### WireBound.Platform.Abstract (Interfaces + Models)

#### Platform Service Interfaces (10)

| Interface | Purpose | Windows | Linux |
|-----------|---------|---------|-------|
| `IPlatformServices` | Factory to register all platform services | WindowsPlatformServices | LinuxPlatformServices |
| `ICpuInfoProvider` | CPU usage % | PerformanceCounter | /proc/stat |
| `IMemoryInfoProvider` | RAM usage | GlobalMemoryStatusEx | /proc/meminfo |
| `IProcessNetworkProvider` | Per-process network | GetExtendedTcpTable | /proc/net/* |
| `IProcessNetworkProviderFactory` | Creates process providers | Factory pattern | Factory pattern |
| `IStartupService` | System startup config | Registry | XDG autostart |
| `IWiFiInfoProvider` | WiFi SSID/signal | ManagedNativeWifi | nmcli/iw |
| `IElevationService` | Admin privilege check | UAC | pkexec |
| `IDnsResolverService` | DNS resolution | System DNS | System DNS |
| `IHelperConnection` / `IHelperProcessManager` | Helper process IPC | Named pipes | Unix sockets |

#### Platform Models (5)

| Model | Purpose |
|-------|---------|
| `ProcessNetworkStats` | Per-process bytes sent/received |
| `ConnectionInfo` | Active connection details |
| `ConnectionStats` | Connection statistics |
| `CpuInfoData` | CPU usage data transfer object |
| `MemoryInfoData` | Memory usage data transfer object |

## Data Flows

<!-- context-init:managed -->

### Network Monitoring Flow

```text
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

```text
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

```text
App.ConfigureServices()
       │
       ├──► StubPlatformServices.Register() ── Always first (defaults)
       │
       ├──► if Windows: WindowsPlatformServices.Register() ── Overrides stubs
       │
       └──► if Linux: LinuxPlatformServices.Register() ── Overrides stubs
```

### System Monitoring Flow

```text
SystemMonitorService (periodic poll)
       │
       ├──► ICpuInfoProvider.GetCpuUsagePercentAsync()
       ├──► IMemoryInfoProvider.GetMemoryInfoAsync()
       │
       ▼
SystemStats aggregated
       │
       ├──► SystemViewModel ──► CircularGauge + history charts
       ├──► OverviewViewModel ──► SystemHealthStrip
       └──► SystemHistoryService ──► HourlySystemStats / DailySystemStats
```

## Database Schema

<!-- context-init:managed -->

| Table | Key Columns | Purpose |
|-------|-------------|---------|
| `HourlyUsages` | Date, Hour, BytesSent, BytesReceived | Hourly network aggregates |
| `DailyUsages` | Date, BytesSent, BytesReceived | Daily network aggregates |
| `HourlySystemStats` | Date, Hour, AvgCpuPercent, AvgMemoryPercent, MaxCpu, MaxMemory | Hourly system stats |
| `DailySystemStats` | Date, AvgCpuPercent, AvgMemoryPercent, MaxCpu, MaxMemory | Daily system stats |
| `AppSettings` | PollingIntervalMs, MinimizeToTray, StartMinimized, ... | User preferences |
| `AppUsageRecords` | ProcessName, Date, BytesSent, BytesReceived | Per-app bandwidth |
| `SpeedSnapshots` | Timestamp, DownloadBps, UploadBps | Speed history for charts |

**Storage**: `LocalApplicationData/WireBound/wirebound.db`

## External Integrations

<!-- context-init:managed -->

| Integration | Purpose | Configuration |
|-------------|---------|---------------|
| SQLite | Local persistence | `LocalApplicationData/WireBound/wirebound.db` |
| Serilog | Structured logging | `LocalApplicationData/WireBound/logs/`, daily rolling, 14-day retention |
| LiveChartsCore | Charting | Initialized in `App.Initialize()` with SkiaSharp backend |
| System.Net.NetworkInterface | Network stats | .NET built-in, cross-platform |

<!-- context-init:user-content-below -->

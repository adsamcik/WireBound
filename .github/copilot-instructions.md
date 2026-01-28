# WireBound Development Instructions

## Project Overview
WireBound is a cross-platform network traffic and system monitoring application built with .NET 10 and Avalonia UI.

## Architecture
- **MVVM Pattern**: Using CommunityToolkit.Mvvm for ViewModels
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **Background Services**: Timer-based network polling service
- **Database**: SQLite with Entity Framework Core
- **Charts**: LiveChartsCore.SkiaSharpView.Avalonia for real-time visualization
- **UI Framework**: Avalonia (cross-platform - Windows, Linux, macOS)
- **Platform Abstraction**: Separate projects for platform-specific implementations

## Project Structure

```
src/
├── WireBound.Core/              # Shared core library
│   ├── Data/                    # Database context (WireBoundDbContext)
│   ├── Helpers/                 # Utility classes (ByteFormatter, ChartColors, LttbDownsampler, CircularBuffer, AdaptiveThresholdCalculator, TrendIndicatorCalculator)
│   ├── Models/                  # Domain models
│   └── Services/                # Service interfaces
│
├── WireBound.Platform.Abstract/ # Platform abstraction layer
│   ├── Models/                  # Platform-specific models (ProcessNetworkStats, ConnectionInfo, CpuInfoData, MemoryInfoData)
│   └── Services/                # Platform service interfaces
│
├── WireBound.Platform.Windows/  # Windows-specific implementations
│   └── Services/                # Windows services (WiFi, Process network, Startup, CPU, Memory, Elevation)
│
├── WireBound.Platform.Linux/    # Linux-specific implementations
│   └── Services/                # Linux services (WiFi, Process network, Startup, CPU, Memory, Elevation)
│
├── WireBound.Platform.Stub/     # Stub implementations for development/testing
│   └── Services/                # Stub service implementations
│
└── WireBound.Avalonia/          # Cross-platform UI application
    ├── Controls/                # Custom controls (CircularGauge, MiniSparkline, SystemHealthStrip)
    ├── Converters/              # XAML value converters (SelectedRowConverter, SpeedUnitConverter)
    ├── Helpers/                 # UI helpers (ChartSeriesFactory, ChartDataManager)
    ├── Services/                # Application services
    ├── Styles/                  # AXAML styles and themes (Colors.axaml, Styles.axaml)
    ├── ViewModels/              # MVVM ViewModels
    └── Views/                   # AXAML views
```

## Key Components

### Services (WireBound.Core)
- `INetworkMonitorService` - Polls network statistics
- `IDataPersistenceService` - Saves data to SQLite
- `INetworkPollingBackgroundService` - Background timer service
- `INavigationService` - View navigation
- `ILocalizationService` - Internationalization
- `ITrayIconService` - System tray functionality
- `IWiFiInfoService` - WiFi connection information
- `IProcessNetworkService` - Per-process network statistics
- `ISystemMonitorService` - System resource monitoring (CPU, Memory)
- `ISystemHistoryService` - Historical system stats management

### Platform Abstractions (WireBound.Platform.Abstract)
- `IPlatformServices` - Platform service factory
- `IProcessNetworkProvider` - Per-process network data provider
- `IProcessNetworkProviderFactory` - Factory for process network providers
- `IWiFiInfoProvider` - WiFi information provider
- `IStartupService` - System startup configuration
- `IDnsResolverService` - DNS resolution
- `IHelperConnection` - Helper process communication
- `IElevationService` - Admin privilege management
- `ICpuInfoProvider` - CPU usage information provider
- `IMemoryInfoProvider` - Memory usage information provider

### Models (WireBound.Core)
- `NetworkStats` - Real-time speed and usage data
- `NetworkAdapter` - Network interface information
- `DailyUsage` / `HourlyUsage` - Historical network usage data
- `DailySystemStats` / `HourlySystemStats` - Historical system stats
- `AppSettings` - User preferences
- `ConnectionInfo` / `ConnectionStats` - Active connection tracking
- `AppUsageRecord` / `AddressUsageRecord` - Per-app and per-address usage
- `SpeedSnapshot` - Point-in-time speed measurement
- `SpeedUnit` - Speed display unit enumeration
- `CpuStats` / `MemoryStats` / `SystemStats` - System resource statistics

### ViewModels (WireBound.Avalonia)
- `MainViewModel` - Navigation control and app state
- `OverviewViewModel` - Quick overview with key metrics (network + system combined)
- `ChartsViewModel` - Real-time interactive chart with time range selection
- `ApplicationsViewModel` - Per-application network usage
- `ConnectionsViewModel` - Active network connections
- `SystemViewModel` - System resource monitoring (CPU, Memory)
- `InsightsViewModel` - Tabbed analytics (Usage, Trends, Correlations)
- `SettingsViewModel` - App configuration

### Views (WireBound.Avalonia)
- `MainWindow` - Main application window
- `OverviewView` - Quick overview dashboard (network + system metrics)
- `ChartsView` - Real-time interactive chart with time range selector
- `ApplicationsView` - Per-application usage tracking
- `ConnectionsView` - Active connections display
- `SystemView` - System resource monitoring with gauges
- `InsightsView` - Tabbed analytics (Usage, Trends, Correlations)
- `SettingsView` - App configuration

### Custom Controls (WireBound.Avalonia)
- `CircularGauge` - Circular gauge for displaying percentage values
- `MiniSparkline` - Compact sparkline chart for inline data visualization
- `SystemHealthStrip` - Status strip showing system health indicators

## Development Guidelines

### Adding New Features
1. Create models in `WireBound.Core/Models`
2. Add service interfaces in `WireBound.Core/Services`
3. For platform-specific features:
   - Add interface in `WireBound.Platform.Abstract/Services`
   - Implement in `WireBound.Platform.Windows/Services` and `WireBound.Platform.Linux/Services`
   - Add stub in `WireBound.Platform.Stub/Services`
4. For UI services, implement in `WireBound.Avalonia/Services`
5. Register services in `App.axaml.cs`
6. Create ViewModels with `ObservableObject` base
7. Create Views in `WireBound.Avalonia/Views`

### Platform-Specific Code
- Windows implementations use Windows APIs (e.g., `netsh`, Windows ETW)
- Linux implementations use Linux tools (e.g., `ss`, `nmcli`, `/proc`)
- Always provide a stub implementation for development and unsupported platforms

### Database Changes
1. Modify `WireBoundDbContext` in `WireBound.Core/Data`
2. Add migrations with EF Core tools
3. Ensure `EnsureCreated()` handles new tables

### Styling
- Styles defined in `WireBound.Avalonia/Styles/`
- Use Avalonia styling system with `{StaticResource}`
- Follow the design system in `docs/DESIGN_SYSTEM.md`

## Build Commands

```powershell
# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Run the application
dotnet run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj

# Publish for distribution
.\scripts\publish.ps1 -Version "1.0.0"

# Publish for specific platform
.\scripts\publish.ps1 -Version "1.0.0" -Runtime linux-x64
```

## Supported Platforms
- Windows x64
- Linux x64
- macOS ARM64 / x64

## Testing Network Accuracy
Download a known file size and compare with app readings.
Cross-platform network monitoring uses .NET's `NetworkInterface` class.

## Workflow
- Commit changes at crucial points:
  - After completing a logical unit of work (e.g., adding a new model, service, or view)
  - After implementing a working feature, even if not fully polished
  - Before starting a risky refactoring or significant change
  - After fixing a bug or resolving an issue
  - When switching context to a different part of the codebase
- Always commit when work is complete with a clear, descriptive commit message
- Use conventional commit format: `type(scope): description` (e.g., `feat(system): add CPU monitoring service`)

# WireBound Development Instructions

## Project Overview
WireBound is a cross-platform network traffic monitoring application built with .NET 10 and Avalonia UI.

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
│   ├── Data/                    # Database context and migrations
│   ├── Helpers/                 # Utility classes (ByteFormatter, ChartColors, LttbDownsampler)
│   ├── Models/                  # Domain models
│   └── Services/                # Service interfaces
│
├── WireBound.Platform.Abstract/ # Platform abstraction layer
│   ├── Models/                  # Platform-specific models (ProcessNetworkStats, ConnectionInfo)
│   └── Services/                # Platform service interfaces
│
├── WireBound.Platform.Windows/  # Windows-specific implementations
│   └── Services/                # Windows services (WiFi, Process network, Startup)
│
├── WireBound.Platform.Linux/    # Linux-specific implementations
│   └── Services/                # Linux services (WiFi, Process network, Startup)
│
├── WireBound.Platform.Stub/     # Stub implementations for development/testing
│   └── Services/                # Stub service implementations
│
└── WireBound.Avalonia/          # Cross-platform UI application
    ├── Converters/              # XAML value converters
    ├── Helpers/                 # UI helpers (ChartSeriesFactory)
    ├── Services/                # Application services
    ├── Styles/                  # AXAML styles and themes
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
- `IElevationService` - Admin privilege management

### Platform Abstractions (WireBound.Platform.Abstract)
- `IPlatformServices` - Platform service factory
- `IProcessNetworkProvider` - Per-process network data provider
- `IWiFiInfoProvider` - WiFi information provider
- `IStartupService` - System startup configuration
- `IDnsResolverService` - DNS resolution
- `IHelperConnection` - Helper process communication

### Models (WireBound.Core)
- `NetworkStats` - Real-time speed and usage data
- `NetworkAdapter` - Network interface information
- `DailyUsage` / `HourlyUsage` - Historical aggregated data
- `AppSettings` - User preferences
- `ConnectionInfo` / `ConnectionStats` - Active connection tracking
- `AppUsageRecord` / `AddressUsageRecord` - Per-app and per-address usage
- `SpeedSnapshot` - Point-in-time speed measurement

### ViewModels (WireBound.Avalonia)
- `MainViewModel` - Navigation control and app state
- `DashboardViewModel` - Real-time stats, charts, time range selection
- `ChartsViewModel` - Advanced chart visualization
- `HistoryViewModel` - Historical data visualization
- `ApplicationsViewModel` - Per-application network usage
- `ConnectionsViewModel` - Active network connections
- `SettingsViewModel` - App configuration

### Views (WireBound.Avalonia)
- `MainWindow` - Main application window
- `DashboardView` - Real-time monitoring with interactive chart
- `ChartsView` - Advanced charting view
- `HistoryView` - Historical usage data
- `ApplicationsView` - Per-application usage tracking
- `ConnectionsView` - Active connections display
- `SettingsView` - App configuration

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
- Commit changes after completing each task or feature effort

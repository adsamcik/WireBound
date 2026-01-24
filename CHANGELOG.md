# Changelog

All notable changes to WireBound will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.5.0] - 2026-01-24

### Added

- **Unified Monitoring Dashboard** - Major redesign with CPU, Memory, and GPU monitoring
  - New Overview view combining network and system metrics in one dashboard
  - New Insights view with tabbed interface for Network Usage, System Trends, and Correlations
  - CircularGauge control for compact radial progress visualization
  - MiniSparkline control for inline trend visualization
  - SystemHealthStrip header showing CPU/RAM/GPU at a glance
- **CPU and RAM Monitoring** - Real-time system resource tracking
  - Windows: PerformanceCounter and GlobalMemoryStatusEx APIs
  - Linux: /proc/stat and /proc/meminfo parsing
  - Platform-specific providers with stub fallback
- **System History Service** - Historical tracking with hourly/daily aggregation
  - HourlySystemStats and DailySystemStats database tables
  - Correlation analysis between network and system metrics
- **Per-App Network Tracking Infrastructure** - Foundation for per-application monitoring
  - IProcessNetworkProvider interface for platform-specific network monitoring
  - Windows: IP Helper API (GetExtendedTcpTable/GetExtendedUdpTable)
  - Linux: /proc/net/* and /proc/[pid]/fd parsing
  - ProcessPollingBackgroundService for coordinating per-app polling
- **Comprehensive Test Suite** - 311 tests for unified monitoring features
  - OverviewViewModelTests, InsightsViewModelTests, SystemHistoryServiceTests
  - ChartColorsTests, ByteFormatterTests, HourlySystemStatsTests
- **Accessibility** - AutomationProperties added to all interactive elements across views
- **Theme System** - Tinted background colors and selection colors extracted to resources

### Changed

- **Navigation Renamed** - Dashboard → Overview, History → Insights
- **Chart Data Management** - New ChartDataManager class with CircularBuffer for O(1) performance
- **Elevation Service** - Moved to Platform.Abstract with improved security patterns
- **Routes Constants** - Eliminated magic strings in navigation with Routes class
- **Today's Usage Display** - Dashboard shows cumulative daily totals instead of session-only
- **Adapter Selector** - Simplified compact horizontal layout

### Fixed

- Database migrations for missing AppSettings columns (StartMinimized, IsPerAppTrackingEnabled, etc.)
- Session totals no longer randomly switch between VPN and physical adapter
- Chart data buffer performance with CircularBuffer replacing List.RemoveAt(0)

## [0.4.0] - 2026-01-20

### Added

- **Linux Startup Support** - Application can now register for autostart on Linux via XDG autostart (.desktop files)
- **Platform Architecture** - New modular platform abstraction layer
  - `WireBound.Platform.Abstract` - Platform interfaces
  - `WireBound.Platform.Stub` - Fallback implementations
  - `WireBound.Platform.Windows` - Windows-specific services
  - `WireBound.Platform.Linux` - Linux-specific services

### Changed

- **Executable Renamed** - Output is now `WireBound.exe` instead of `WireBound.Avalonia.exe`
- **Refactored Chart Logic** - Centralized chart styling via ChartSeriesFactory
- **DI-based Views** - Views now created through IViewFactory for better testability
- **Improved Lifecycle** - Proper disposal and event unsubscription on shutdown

### Fixed

- Windows startup now properly integrates with Windows Settings > Apps > Startup
- Detects when startup is disabled by user or policy in Windows Settings

## [0.3.0] - 2026-01-16

### Added

- **Adapter Dashboard** - New dashboard section showing all active network adapters with real-time per-adapter traffic stats
- **WiFi Detection** - Cross-platform WiFi info service showing SSID, signal strength, channel, and frequency band
  - Windows: Native WiFi API via ManagedNativeWifi
  - Linux: nmcli/iw command parsing
  - macOS: airport command parsing
- **Tethering Detection** - Automatic detection and labeling of USB and Bluetooth tethered connections
  - Supports Android RNDIS, iPhone USB, and generic USB Ethernet adapters
  - Bluetooth PAN/BNEP interface detection
- **Adapter Badges** - Visual badges showing adapter type (VPN, USB, BT, VM) and WiFi signal percentage
- **History View Enhancements**
  - Column sorting for all data columns
  - Custom date range picker
  - Data cap tracking and progress display
  - Row selection with visual feedback
  - Responsive layout with panel animations
  - Loading/error states with improved export handling
  - Analytics charts and drill-down functionality

### Changed

- **Simplified VPN Panel** - Now shows only VPN traffic speeds and session totals (removed unreliable overhead calculation)
- VPN panel visibility now based on connection status rather than active traffic
- Speed Unit setting changed from toggle to dropdown selector
- Dashboard content now scrollable for smaller screens

### Fixed

- VPN panel flickering when traffic fluctuated (added sticky visibility)
- Database schema migration for UseSpeedInBits column
- Solution nested folder structure causing NuGet restore warning

## [0.2.0] - 2026-01-14

### Changed

- **Migrated from MAUI to Avalonia UI** - Now fully cross-platform (Windows, Linux, macOS)
- Simplified project structure (removed WireBound.Windows)
- Updated CI/CD pipelines to use Linux runners
- Reduced build size by ~55% (108 MB → 49 MB)

### Fixed

- Database migration for existing databases with new schema columns
- Publishing pipeline now correctly implements GetAdapters interface
- Resolved all build warnings
- Fixed RuntimeIdentifier for ReadyToRun compilation

### Removed

- Nightly builds workflow
- MAUI-specific code and dependencies
- Windows-only services (replaced with cross-platform alternatives)

### Technical

- Built with .NET 10 and Avalonia UI (cross-platform)
- MVVM architecture with CommunityToolkit.Mvvm
- LiveCharts2 for real-time charting
- Entity Framework Core with SQLite
- Serilog for structured logging

## [0.0.1] - 2026-01-13

### Added

- Initial release of WireBound
- Real-time network traffic monitoring dashboard
- Download/Upload speed visualization with interactive charts
- Time range selection (1 min, 5 min, 15 min, 1 hour)
- Historical usage tracking (hourly and daily)
- Per-application network usage monitoring
- System tray integration with minimize-to-tray
- Dark/Light theme support
- Network adapter selection
- SQLite database for persistent storage
- Adaptive chart scaling with automatic threshold detection

### Technical

- Built with .NET 10 and Avalonia UI (cross-platform)
- MVVM architecture with CommunityToolkit.Mvvm
- LiveCharts2 for real-time charting
- Entity Framework Core with SQLite
- Serilog for structured logging

## [1.0.0] - TBD

- Initial public release

---

## Release Types

- **Major (X.0.0)**: Breaking changes or major new features
- **Minor (0.X.0)**: New features, backwards compatible
- **Patch (0.0.X)**: Bug fixes and minor improvements

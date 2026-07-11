# Changelog

All notable changes to WireBound will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.8.0] - 2026-07-11

### Security

- **Mutual IPC Authentication** - Server signs the session expiry with the shared secret (HMAC) and includes a `ServerSignature` in `AuthenticateResponse`; both client connections verify the signature before accepting the session, preventing pipe/socket squatting attacks
- **Elevation Hardening** - Symlink/TOCTOU protections in `SecretManager` and `SessionManager`, systemd sandboxing and UID tracking for `LinuxHelperProcessManager`, ACL validation and reparse point checks for `WindowsHelperProcessManager`, graceful shutdown message type
- **Path Traversal Protection** - CSV export paths are validated and constrained to the `.csv` extension; Linux helper binary path is validated to stay within the app directory before `pkexec` elevation
- **Auth Failure Tracking** - `HelperServer` now tracks authentication failures for rate limiting

### Added

- **Self-Updater (Velopack)** - In-app update download, apply, and restart for installed mode (Windows Setup.exe / Linux AppImage) with delta updates and automatic rollback
- **Auto-Download Updates** - Background download when update detected; respects metered network detection; restart always requires user action
- **What's New Dialog** - Post-update dialog showing release notes fetched from GitHub, displayed once after restart
- **Navigation Badge** - Settings nav icon shows dot indicator when update is available
- **Tray Menu Update Item** - "Update available" item in system tray context menu, navigates to Settings
- **Metered Network Detection** - Platform-native detection (Windows WinRT ConnectionCost, Linux nmcli) with Platform.Abstract interface and Stub fallback
- **Portable Mode Fallback** - Non-installed builds show "View Release" link instead of download button; `IsUpdateSupported` distinguishes modes
- **Memory Pressure Monitoring** - Sustained-threshold detection in the network polling background service, configurable threshold/cooldown/floor in Settings, system tray tint and tooltip, breathing pulse animation on the health strip, and messenger-based alerts across ViewModels
- **System Snapshot Persistence** - New `SystemSnapshot` / `MemoryPressureEvent` history tables and repository for historical memory-pressure analysis, surfaced in Charts and System views
- **Elevated Helper Auto-Start** - Register the elevation helper to start at OS login without a UAC/password prompt on every boot (Windows Task Scheduler "highest privileges"; Linux systemd user service), with a toggle in Settings
- **Native AOT for Elevation & IPC** - Elevation helpers and the IPC library now publish as trimmed, Native AOT binaries backed by a source-generated MessagePack resolver (no reflection); Windows helper AOT publish is 6.96 MB
- **Auto Network Adapter Detection** - Automatically selects the primary internet adapter via default gateway routing, with VPN/secondary adapter traffic overlaid as additional chart series and compact stats, plus an auto-switch notification banner
- **Resource Insights Improvements** - Category-colored bars matching the donut palette, search/sort for Top Apps, adaptive summary cards per Memory/CPU toggle, cached merged app data for instant search without re-polling
- **Process Resource Providers & INetworkCostProvider** - New platform abstractions for process-level resource stats and network cost/metering

### Changed

- Broad refactor across security, services, UI, and platform layers (`TimeProvider` injection, `IUiDispatcher` abstraction for testable UI-thread dispatch, `DynamicResource` → `StaticResource` migration, view-aware update coalescing, async chart downsampling)
- Simplified system health strip indicators and improved chart usability
- CI restore no longer hard-fails on lock-file/SDK-patch drift (`--locked-mode` dropped); added a Native AOT validation gate

### Fixed

- **Velopack Packaging** - `release.yml` and `publish.ps1` referenced the stale executable name `WireBound.Avalonia(.exe)`; the app was renamed to `WireBound(.exe)` in a prior release, so Velopack `vpk pack` was silently failing to find the entry executable (masked by `|| true`), preventing installer/AppImage packages from being produced
- **Elevation Helper Crash on .NET 10** - Removed an invalid `PipeOptions` flag (`PIPE_REJECT_REMOTE_CLIENTS`) that crashed the helper immediately on pipe creation
- **Database Migrations** - Rewrote `ApplyMigrations` with comprehensive, idempotent schema coverage and fixed a connection-lifecycle bug that could destroy in-memory test databases
- **Per-App Tracking** - Persist per-app network stats correctly and fix ETW byte counter accumulation across process restarts
- **DNS Resolver Cache** - Remove existing LRU entry before re-adding to prevent duplicate cache entries
- **Deadlock Prevention** - Move `StatsUpdated` event invocation outside the lock in the network monitor
- **Tray/Shutdown** - OS shutdown now closes the app instead of hiding it to tray
- **Polling Interval** - `PeriodicTimer` is recreated on interval change so new intervals take effect immediately
- Numerous smaller fixes: async locking in process tracking, disposed-state checks in `ConnectionsViewModel`, non-blocking `DnsResolverService` disposal, Serilog wired to DI `ILogger<T>`, hardened JSON parsing when fetching update release notes, Insights UI layout/chart-type fixes

### Dependencies

- Bump TUnit from 1.12.93 to 1.17.54
- Bump AwesomeAssertions from 9.3.0 to 9.4.0
- Bump Microsoft.Extensions.DependencyInjection.Abstractions from 10.0.2 to 10.0.3
- Bump Microsoft.Diagnostics.Tracing.TraceEvent from 3.1.29 to 3.1.30
- Bump GitHub Actions: actions/download-artifact (7→8), actions/upload-artifact (6→7), softprops/action-gh-release (2→3)

## [0.7.0] - 2026-02-08

### Security

- **IPC Security Infrastructure** - SecretManager for shared secret storage with OS-level file protection, SessionManager with expiry and concurrency limits, RateLimiter with sliding-window per-client rate limiting
- **Critical Fixes** - Windows pipe ACL uses caller SID, ValidateExecutablePath fails closed, Linux socket permissions 0600 with chmod before listen, Windows secret file gets explicit ACL (current user + SYSTEM only)
- **High Severity Fixes** - Auth rate limiting with per-client failure tracking and auto-disconnect, MessagePack UntrustedData security mode, IPC receive timeout (30s default), connection TOCTOU mitigation, factory race fix with Volatile.Read/Write, SessionManager TOCTOU fix with SemaphoreSlim
- **Linux SO_PEERCRED Validation** - PID/UID validation against launcher for Unix socket connections
- **IPv6 Parsing Fix** - Corrected /proc/net/tcp6 parsing with 4×32-bit little-endian groups

### Added

- **IPC Library** - Binary IPC library for helper process communication using MessagePack protocol with length-prefixed framing
- **Windows Elevation Helper** - Elevated helper process with ETW-based network connection tracking
- **Linux Elevation Helper** - Elevated helper process with /proc-based connection tracking
- **Helper Lifecycle Management** - Task Scheduler integration (Windows) and systemd service (Linux)
- **Elevated Provider Integration** - Elevated network providers wired into platform service factories with retry logic and real-time polling
- **Provider Hot-Swap** - ProcessNetworkService handles runtime provider changes atomically with auto-start
- **Clipboard Copy** - CopyToClipboardAsync implemented using Avalonia clipboard API
- **StubDnsResolverService** - Stub fallback for DNS resolver platform service
- **Theme Support** - Dark/light theme support with accent button style
- **Responsive Navigation Rail** - Collapsible navigation rail with responsive layout
- **Update Check Service** - GitHub releases-based update checking
- **Settings Enhancements** - Theme selector, update check toggle, data export/backup in Settings view
- **Data Export Service** - CSV export for daily/hourly usage data via Settings → Data Management
- **Database Backup** - One-click SQLite backup via Settings → Data Management using safe online backup API
- **AddressUsageRecord Persistence** - Per-address network usage now tracked in database with indexes
- **Accent Button Style** - New `Button.Accent` style for action buttons
- **AccentBrush / SuccessBgTintBrush Resources** - New brush resources for accent-colored text and success backgrounds
- **Docker Test Infrastructure** - Dockerfile and script for running Linux-specific tests in containers
- **Comprehensive Test Suite** - 1194 tests covering elevation, IPC, security, services, helpers, converters, and models

### Changed

- **IPC Protocol** - Migrated from JSON to MessagePack binary serialization with MessageType enum replacing string-based types
- **Overview Layout** - Removed GPU metrics, added responsive layout
- **Removed Obsolete ViewModels** - Removed Dashboard and History ViewModels in favor of Overview and Insights
- **CI/CD** - Added Helper publish step, removed macOS target
- **Applications/Connections Status** - Changed 'COMING SOON' badges to 'LIMITED' to reflect implemented helper-based tracking

### Fixed

- Converter `ConvertBack` methods no longer throw `NotImplementedException`
- Localization service initialized at app startup (`Strings.Initialize`)
- Database migration for `AddressUsageRecords` table on existing databases
- Database backup uses SQLite online backup API instead of file copy (handles locked databases)
- Export/backup buttons guarded against double-click with `IsExporting` flag
- Data export handles null/empty data gracefully with logging
- Connection timestamps use `DateTime.Now` instead of `DateTime.UtcNow` for local desktop context

## [0.6.0] - 2026-02-01

### Security

- **Helper-Based Elevation** - Replaced full-app elevation with minimal helper process pattern
  - Removed legacy full-app elevation code for improved security
  - Updated all UI text to reflect new helper-based elevation approach

### Added

- **System Stats Persistence** - CPU and RAM statistics now recorded to database for historical analysis

### Changed

- **NuGet Central Package Management** - Migrated to centralized package version management
- **Testing Framework** - Migrated from FluentAssertions to AwesomeAssertions
- **Major Refactoring** - Implemented Phases 2-4 of the refactoring roadmap
- **.NET SDK** - Updated to .NET SDK 10.0.102

### Fixed

- **Memory Leaks** - Critical memory leak fixes with Phase 1 disposal implementation
- **CI/CD** - Build now uses global.json for consistent SDK version across environments
- **Code Style** - Fixed whitespace formatting across codebase

### Dependencies

- Bump AwesomeAssertions from 9.0.0 to 9.3.0
- Bump Avalonia and Avalonia.Desktop to latest versions
- Bump GitHub Actions: actions/cache (4→5), actions/upload-artifact (4→6), actions/checkout (4→6), actions/setup-dotnet (4→5), actions/download-artifact (4→7)

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
  - NetworkPollingBackgroundService for coordinating per-app polling
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

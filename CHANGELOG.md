# Changelog

All notable changes to WireBound will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-01-14

### Changed

- **Migrated from MAUI to Avalonia UI** - Now fully cross-platform (Windows, Linux, macOS)
- Simplified project structure (removed WireBound.Windows)
- Updated CI/CD pipelines to use Linux runners
- Reduced build size by ~55% (108 MB → 49 MB)

### Fixed

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

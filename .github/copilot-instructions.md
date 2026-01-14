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

## Project Structure

```
src/
├── WireBound.Core/           # Shared core library
│   ├── Data/                 # Database context and migrations
│   ├── Helpers/              # Utility classes
│   ├── Models/               # Domain models
│   └── Services/             # Service interfaces
│
└── WireBound.Avalonia/       # Cross-platform UI application
    ├── Services/             # Platform implementations
    ├── Styles/               # AXAML styles and themes
    ├── ViewModels/           # MVVM ViewModels
    └── Views/                # AXAML views
```

## Key Components

### Services (WireBound.Core)
- `INetworkMonitorService` - Polls network statistics
- `IDataPersistenceService` - Saves data to SQLite
- `NetworkPollingBackgroundService` - Background timer service

### Models (WireBound.Core)
- `NetworkStats` - Real-time speed and usage data
- `NetworkAdapter` - Network interface information
- `DailyUsage` / `HourlyUsage` - Historical aggregated data
- `AppSettings` - User preferences

### ViewModels (WireBound.Avalonia)
- `MainViewModel` - Navigation control
- `DashboardViewModel` - Real-time stats, charts, time range selection
- `HistoryViewModel` - Historical data visualization
- `SettingsViewModel` - App configuration

### Views (WireBound.Avalonia)
- `DashboardView` - Real-time monitoring with interactive chart
- `HistoryView` - Historical usage data
- `SettingsView` - App configuration

## Development Guidelines

### Adding New Features
1. Create models in `WireBound.Core/Models`
2. Add service interfaces in `WireBound.Core/Services`
3. Implement services in `WireBound.Avalonia/Services`
4. Register services in `App.axaml.cs`
5. Create ViewModels with `ObservableObject` base
6. Create Views in `WireBound.Avalonia/Views`

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

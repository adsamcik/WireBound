# CLAUDE.md

## Project Overview

WireBound is a privacy-focused, cross-platform network traffic and system monitoring application built with .NET 10 and Avalonia UI. It provides real-time network speed monitoring, per-application tracking, CPU/RAM monitoring, and historical data visualization—all stored locally with no cloud telemetry.

## Tech Stack

- **Language**: C# / .NET 10
- **UI Framework**: Avalonia UI (cross-platform: Windows, Linux, macOS)
- **Architecture**: MVVM with CommunityToolkit.Mvvm
- **DI**: Microsoft.Extensions.DependencyInjection
- **Database**: SQLite with Entity Framework Core
- **Charts**: LiveChartsCore.SkiaSharpView.Avalonia
- **Logging**: Serilog
- **Testing**: xUnit, Moq, AwesomeAssertions

## Architecture

```text
┌─────────────────────────────────────────────────────────────────────┐
│                        WireBound.Avalonia                           │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐  │
│  │    Views    │  │  ViewModels │  │  Services   │  │  Controls  │  │
│  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘  │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────┼──────────────────────────────────────┐
│                    WireBound.Core                                    │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌────────────┐  │
│  │   Models    │  │  Services   │  │    Data     │  │  Helpers   │  │
│  │             │  │ (Interfaces)│  │ (DbContext) │  │            │  │
│  └─────────────┘  └─────────────┘  └─────────────┘  └────────────┘  │
└──────────────────────────────┬──────────────────────────────────────┘
                               │
┌──────────────────────────────┼──────────────────────────────────────┐
│               WireBound.Platform.Abstract                            │
│  ┌────────────────────────────────────────────────────────────────┐ │
│  │  Platform Interfaces (ICpuInfoProvider, IMemoryInfoProvider,   │ │
│  │  IProcessNetworkProvider, IStartupService, IElevationService)  │ │
│  └────────────────────────────────────────────────────────────────┘ │
└──────────────────────────────┬──────────────────────────────────────┘
           ┌───────────────────┼───────────────────┐
           ▼                   ▼                   ▼
┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│ Platform.Windows │  │  Platform.Linux  │  │  Platform.Stub   │
│  (Windows APIs)  │  │ (/proc, nmcli)   │  │   (Fallbacks)    │
└──────────────────┘  └──────────────────┘  └──────────────────┘
```

See `.github/context/ARCHITECTURE.md` for detailed component maps.

## Development Commands

```powershell
# Restore dependencies
dotnet restore

# Build all projects
dotnet build

# Run the application
dotnet run --project src/WireBound.Avalonia/WireBound.Avalonia.csproj

# Run tests
dotnet test

# Publish for distribution
.\scripts\publish.ps1 -Version "1.0.0"

# Publish for specific platform
.\scripts\publish.ps1 -Version "1.0.0" -Runtime linux-x64
```

## Key Patterns

1. **MVVM with Source Generators**: ViewModels inherit from `ObservableObject`, use `[ObservableProperty]` and `[RelayCommand]` attributes
2. **Platform Service Factory**: Each platform project has a `*PlatformServices.cs` that registers platform-specific implementations
3. **Interface Segregation**: Data persistence uses segregated interfaces (`INetworkUsageRepository`, `IAppUsageRepository`, etc.)
4. **Routes as Constants**: Navigation uses `Routes` class for type-safe route names
5. **View Factory Pattern**: Views created via `IViewFactory` for DI and testability

## Code Conventions

- **File naming**: PascalCase for all C# files (e.g., `NetworkAdapter.cs`, `OverviewViewModel.cs`)
- **Interfaces**: Prefix with `I` (e.g., `INetworkMonitorService`)
- **ViewModels**: Suffix with `ViewModel` (e.g., `OverviewViewModel`)
- **Views**: Suffix with `View` or `Window` (e.g., `OverviewView`, `MainWindow`)
- **Platform services**: Platform prefix (e.g., `WindowsCpuInfoProvider`, `LinuxMemoryInfoProvider`)
- **Tests**: Suffix with `Tests` (e.g., `OverviewViewModelTests`)

## Important Files

| File | Purpose |
| ---- | ------- |
| `src/WireBound.Avalonia/App.axaml.cs` | Application entry, DI configuration |
| `src/WireBound.Core/Data/WireBoundDbContext.cs` | Entity Framework database context |
| `src/WireBound.Core/Routes.cs` | Navigation route constants |
| `src/WireBound.Platform.Abstract/IPlatformServices.cs` | Platform service factory interface |
| `src/WireBound.Avalonia/Styles/Colors.axaml` | Design system color definitions |
| `docs/DESIGN_SYSTEM.md` | Complete UI/UX design guidelines |

## Gotchas

- **Database migrations**: Use `context.ApplyMigrations()` for manual migrations; EF migrations not used
- **Platform-specific code**: Must annotate with `[SupportedOSPlatform("windows")]` or `[SupportedOSPlatform("linux")]`
- **LiveCharts thread safety**: Chart updates must happen on UI thread via `Dispatcher.UIThread`
- **Stub implementations required**: Every platform interface needs a stub in `WireBound.Platform.Stub`
- **Singleton ViewModels**: Most ViewModels are registered as singletons; Views are transient

## Agent Instructions

When working in this codebase:

1. **Follow the platform abstraction pattern**: New platform features need interfaces in `Platform.Abstract`, implementations in `Platform.Windows` and `Platform.Linux`, and stubs in `Platform.Stub`
2. **Use existing helpers**: `ByteFormatter`, `ChartColors`, `CircularBuffer`, `LttbDownsampler`, `TrendIndicatorCalculator` exist in `WireBound.Core/Helpers`
3. **Register services in App.axaml.cs**: All DI registration happens in `ConfigureServices()`
4. **Follow the design system**: Use colors from `Colors.axaml`, follow spacing from `docs/DESIGN_SYSTEM.md`
5. **Commit at logical points** using conventional commit format: `type(scope): description`
6. **Run tests before completing work**: `dotnet test`
7. **Check for accessibility**: Add `AutomationProperties` to interactive elements
